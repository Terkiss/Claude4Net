using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Runtime
{
    public class ToolOrchestrator : IToolRegistry
    {
        private readonly List<ITool> _coreTools;
        private readonly List<ITool> _dynamicTools = new List<ITool>();
        private readonly IUserApprovalHandler? _approvalHandler;
        private readonly IServiceProvider _serviceProvider;

        public ToolOrchestrator(IEnumerable<ITool> coreTools, IUserApprovalHandler? approvalHandler, IServiceProvider serviceProvider)
        {
            _coreTools = coreTools.ToList();
            _approvalHandler = approvalHandler;
            _serviceProvider = serviceProvider;
        }

        public void ReloadDynamicPlugins(string directoryPath)
        {
            _dynamicTools.Clear();
            if (!Directory.Exists(directoryPath)) return;
            
            foreach (var dllPath in Directory.GetFiles(directoryPath, "*.dll"))
            {
                try
                {
                    byte[] rawAssembly = File.ReadAllBytes(dllPath);
                    var assembly = System.Reflection.Assembly.Load(rawAssembly);
                    var toolTypes = assembly.GetTypes().Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    
                    foreach(var type in toolTypes)
                    {
                        var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type) as ITool;
                        if (instance != null) _dynamicTools.Add(instance);
                    }
                }
                catch { } // Ignore faulty DLLs safely
            }
        }

        public void AddTool(ITool tool)
        {
            if (!_coreTools.Any(t => t.Name == tool.Name)) _coreTools.Add(tool);
        }

        public IReadOnlyList<ITool> GetTools() => _coreTools.Concat(_dynamicTools).ToList();

        public ITool? GetTool(string name)
        {
            return _coreTools.Concat(_dynamicTools).FirstOrDefault(t => 
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                (t.Aliases != null && t.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))));
        }

        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context, CancellationToken ct = default)
        {
            var tool = GetTool(request.Name);
            if (tool == null) return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };

            try
            {
                string jsonInput = JsonSerializer.Serialize(request.Input);

                bool isYolo = AppState.CurrentPermissionMode == PermissionMode.Yolo || 
                              AppState.CurrentPermissionMode == PermissionMode.BypassPermissions;
                
                bool isSensitive = IsSensitiveTool(tool.Name);
                int safetyLevel = GetPathSafetyLevel(request.Input);

                // Safety Levels: 0 = Dangerous/Illegal, 1 = Safe (System), 2 = Safe (Workspace), 3 = Not Applicable

                // --- STRICT WORKSPACE SANDBOXING ---
                if (safetyLevel == 0) // Outside everything
                {
                    if (isYolo)
                    {
                        if (_approvalHandler != null)
                        {
                            AnsiConsole.MarkupLine("[bold yellow]⚠ Warning: Target is OUTSIDE both Workspace and System storage. YOLO downgraded to 'Normal'.[/]");
                            bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                            if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied outside-access.", IsError = true };
                        }
                    }
                    else
                    {
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Security Error: Access denied. Target is outside workspace. Use /setworkspace or !yolo.", IsError = true };
                    }
                }
                else if (safetyLevel == 2) // Workspace
                {
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Error: Workspace not set. Use /setworkspace <path> first.", IsError = true };

                    if (!isYolo && isSensitive && _approvalHandler != null)
                    {
                        bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied permission.", IsError = true };
                    }
                }
                // safetyLevel == 1 (System) is always allowed for internal agent functions

                var result = await tool.ExecuteAsync(jsonInput, context, ct);
                return new ToolUseResult { ToolUseId = request.Id, Content = result, IsError = false };
            }
            catch (OperationCanceledException)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = "Execution Cancelled by User.", IsError = true };
            }
            catch (Exception ex)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Execution Error: {ex.Message}", IsError = true };
            }
        }

        private bool IsSensitiveTool(string name)
        {
            var sensitivePrefixes = new[] { "bash", "write", "edit", "delete", "shell", "sh" };
            return sensitivePrefixes.Any(p => name.ToLower().Contains(p));
        }

        private int GetPathSafetyLevel(object? input)
        {
            if (input == null) return 3;

            try
            {
                var json = JsonSerializer.Serialize(input);
                using var doc = JsonDocument.Parse(json);
                return CheckElementSafety(doc.RootElement);
            }
            catch { return 0; } 
        }

        private int CheckElementSafety(JsonElement element)
        {
            int minSafety = 3;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    int s;
                    // Special handling for command strings in bash/shell tools
                    if (prop.Name.Equals("command", StringComparison.OrdinalIgnoreCase) || 
                        prop.Name.Equals("sql", StringComparison.OrdinalIgnoreCase))
                    {
                        s = CheckCommandSafety(prop.Value.GetString());
                    }
                    else
                    {
                        s = CheckElementSafety(prop.Value);
                    }
                    if (s < minSafety) minSafety = s;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    int s = CheckElementSafety(item);
                    if (s < minSafety) minSafety = s;
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                int s = EvaluateSinglePathSafety(element.GetString());
                if (s < minSafety) minSafety = s;
            }

            return minSafety;
        }

        private int CheckCommandSafety(string? cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return 3;

            // Heuristic: If it contains .. or starts with a root (/, C:\, etc.) anywhere after space or at start
            // This prevents "cat /etc/passwd" or "ls ../../"
            string[] tokens = cmd.Split(new[] { ' ', '\t', '|', '>', '<', '&', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                string t = token.Trim('\'', '\"');
                if (t.Contains("..") || Path.IsPathRooted(t))
                {
                    // Check if the rooted path happens to be inside our safe zones
                    int s = EvaluateSinglePathSafety(t);
                    if (s == 0) return 0;
                }
            }
            return 2; // Assume workspace-safe if no explicit escape found
        }

        private int EvaluateSinglePathSafety(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return 3;

            try
            {
                // Handle URI format (file:///)
                if (targetPath.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = new Uri(targetPath).LocalPath;
                }

                // If it doesn't look like a path (no slashes or dots), assume safe content
                if (!targetPath.Contains(Path.DirectorySeparatorChar) && 
                    !targetPath.Contains(Path.AltDirectorySeparatorChar) && 
                    !targetPath.Contains("..")) return 3;

                string fullPath = Path.GetFullPath(targetPath);
                
                bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
                var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

                // 1. Check Restricted System Storage (db and Skills only)
                string sysPath = Path.GetFullPath(AppState.SystemBaseDir);
                string normSys = sysPath.EndsWith(Path.DirectorySeparatorChar) ? sysPath : sysPath + Path.DirectorySeparatorChar;

                if (fullPath.StartsWith(normSys, comparison) || fullPath.Equals(sysPath, comparison))
                {
                    // Within system dir, but we ONLY allow specific sub-folders for the agent
                    if (fullPath.Contains($"{Path.DirectorySeparatorChar}db{Path.DirectorySeparatorChar}") || 
                        fullPath.Contains($"{Path.DirectorySeparatorChar}Skills{Path.DirectorySeparatorChar}") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}db") ||
                        fullPath.EndsWith($"{Path.DirectorySeparatorChar}Skills"))
                    {
                        return 1; // Safe System Access
                    }
                    return 0; // Dangerous System Access (trying to touch app files)
                }

                // 2. Check User Workspace
                if (!string.IsNullOrEmpty(AppState.CurrentCwd))
                {
                    string wsPath = Path.GetFullPath(AppState.CurrentCwd);
                    string normWs = wsPath.EndsWith(Path.DirectorySeparatorChar) ? wsPath : wsPath + Path.DirectorySeparatorChar;
                    
                    if (fullPath.StartsWith(normWs, comparison) || fullPath.Equals(wsPath, comparison))
                    {
                        if (AppState.CurrentPermissionMode != PermissionMode.Yolo)
                        {
                            if (fullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}") || 
                                fullPath.Contains($"{Path.DirectorySeparatorChar}.gemini{Path.DirectorySeparatorChar}"))
                                return 0;
                        }
                        return 2; // Safe Workspace Access
                    }
                }

                return 0; // Outside
            }
            catch { return 0; } 
        }

        public async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context, CancellationToken ct = default)
        {
            var results = new List<ToolUseResult>();
            var concurrentRequests = new List<ToolUseRequest>();
            var sequentialRequests = new List<ToolUseRequest>();

            foreach (var req in requests)
            {
                var tool = GetTool(req.Name);
                if (tool != null && tool.IsConcurrencySafe)
                {
                    concurrentRequests.Add(req);
                }
                else
                {
                    sequentialRequests.Add(req);
                }
            }

            // Execute concurrency-safe tools in parallel
            if (concurrentRequests.Any())
            {
                var concurrentTasks = concurrentRequests.Select(req => ExecuteToolAsync(req, context, ct));
                var concurrentResults = await Task.WhenAll(concurrentTasks);
                results.AddRange(concurrentResults);
            }

            // Execute others sequentially
            foreach (var req in sequentialRequests)
            {
                if (ct.IsCancellationRequested) break;
                var result = await ExecuteToolAsync(req, context, ct);
                results.Add(result);
            }

            return results;
        }
    }
}
