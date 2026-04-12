using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.SDK;

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

        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context)
        {
            var tool = GetTool(request.Name);
            if (tool == null) return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };

            try
            {
                string jsonInput = JsonSerializer.Serialize(request.Input);

                bool isFullAuto = AppState.CurrentPermissionMode == PermissionMode.Yolo || 
                                 AppState.CurrentPermissionMode == PermissionMode.BypassPermissions;
                
                bool isSensitive = IsSensitiveTool(tool.Name);
                bool isPathSafe = IsPathSafe(request.Input);

                // Task 4.2: YOLO mode security fallback
                // Even in YOLO mode, if the tool is sensitive AND the path is unsafe, require approval.
                if (_approvalHandler != null && isSensitive)
                {
                    bool needsApproval = !isFullAuto || !isPathSafe;
                    if (needsApproval)
                    {
                        bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied permission (Potential security risk: Outside workspace).", IsError = true };
                    }
                }

                var result = await tool.ExecuteAsync(jsonInput, context);
                return new ToolUseResult { ToolUseId = request.Id, Content = result, IsError = false };
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

        private bool IsPathSafe(object? input)
        {
            if (input == null) return true;

            try
            {
                var json = JsonSerializer.Serialize(input);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? targetPath = null;
                if (root.TryGetProperty("file_path", out var fp)) targetPath = fp.GetString();
                else if (root.TryGetProperty("path", out var p)) targetPath = p.GetString();

                if (string.IsNullOrEmpty(targetPath)) return true;

                string fullPath = Path.GetFullPath(targetPath);
                string workspacePath = Path.GetFullPath(AppState.CurrentCwd);

                return fullPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; } // Fallback to safe if parsing fails? Or maybe false for security?
        }

        public async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context)
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
                var concurrentTasks = concurrentRequests.Select(req => ExecuteToolAsync(req, context));
                var concurrentResults = await Task.WhenAll(concurrentTasks);
                results.AddRange(concurrentResults);
            }

            // Execute others sequentially
            foreach (var req in sequentialRequests)
            {
                var result = await ExecuteToolAsync(req, context);
                results.Add(result);
            }

            return results;
        }
    }
}
