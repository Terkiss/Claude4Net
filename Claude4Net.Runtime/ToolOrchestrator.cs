using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
                
                var evaluator = new PathSafetyEvaluator();
                var safetyResult = evaluator.EvaluateInputSafety(request.Input);

                // --- STRICT WORKSPACE SANDBOXING ---
                if (safetyResult == PathSafetyResult.Outside) // Outside everything
                {
                    // Even in YOLO mode, we require explicit confirmation for actions outside the sandbox
                    if (_approvalHandler != null)
                    {
                        AnsiConsole.MarkupLine("[bold red]⚠ SECURITY ALERT: Attempting to access file OUTSIDE the workspace/system sandbox![/]");
                        AnsiConsole.MarkupLine($"[yellow]Tool:[/] {tool.Name}");
                        AnsiConsole.MarkupLine($"[yellow]YOLO status:[/] {(isYolo ? "Downgraded to 'Manual Approval' for safety." : "Manual Approval Required.")}");
                        
                        bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied outside-access. Security policy enforced.", IsError = true };
                    }
                    else
                    {
                        string modeInfo = isYolo ? " (YOLO mode does not bypass sandbox without approval handler)" : "";
                        return new ToolUseResult { ToolUseId = request.Id, Content = $"Security Error: Outside access requested but no approval handler available. Denied.{modeInfo}", IsError = true };
                    }
                }
                else if (safetyResult == PathSafetyResult.Workspace) // Workspace
                {
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Error: Workspace not set. Use /setworkspace <path> first.", IsError = true };

                    if (!isYolo && isSensitive && _approvalHandler != null)
                    {
                        bool approved = await _approvalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        if (!approved) return new ToolUseResult { ToolUseId = request.Id, Content = "User denied permission.", IsError = true };
                    }
                }
                // safetyResult == SafeSystem or NotApplicable is allowed for internal agent functions (Skills/db)

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
