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
using TeruTeruPandas.Core;

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

        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
        {
            var tool = GetTool(request.Name);
            if (tool == null) return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };

            string jsonInput = JsonSerializer.Serialize(request.Input);
            var evaluator = new PathSafetyEvaluator();
            var safetyResult = evaluator.EvaluateInputSafety(request.Input);
            bool isYolo = AppState.CurrentPermissionMode == PermissionMode.Yolo || 
                          AppState.CurrentPermissionMode == PermissionMode.BypassPermissions;
            bool isSensitive = IsSensitiveTool(tool.Name);
            var activeApprovalHandler = overrideHandler ?? _approvalHandler;
            bool? approved = null;

            try
            {
                // --- STRICT WORKSPACE SANDBOXING ---
                if (safetyResult == PathSafetyResult.Outside) // Outside everything
                {
                    if (isYolo)
                    {
                        // In YOLO mode, we allow manual approval for actions outside the sandbox
                        if (activeApprovalHandler != null)
                        {
                            AnsiConsole.MarkupLine("[bold red]⚠ SECURITY ALERT: Attempting to access file OUTSIDE the workspace/system sandbox![/]");
                            AnsiConsole.MarkupLine($"[yellow]Tool:[/] {tool.Name}");
                            AnsiConsole.MarkupLine("[yellow]YOLO status:[/] Downgraded to 'Manual Approval' for safety.");
                            
                            approved = await activeApprovalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                            if (approved != true)
                            {
                                await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Denied");
                                return new ToolUseResult { ToolUseId = request.Id, Content = "User denied outside-access. Security policy enforced.", IsError = true };
                            }
                        }
                        else
                        {
                            await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Denied (No Handler)");
                            return new ToolUseResult { ToolUseId = request.Id, Content = "Security Error: Outside access requested in YOLO mode but no approval handler available. Denied.", IsError = true };
                        }
                    }
                    else
                    {
                        // In Normal mode, outside access is strictly forbidden
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Forbidden");
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Security Error: Access to paths outside the workspace is strictly prohibited in Normal mode.", IsError = true };
                    }
                }
                else if (safetyResult == PathSafetyResult.Workspace) // Workspace
                {
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Error (Workspace Not Set)");
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Error: Workspace not set. Use /setworkspace <path> first.", IsError = true };
                    }

                    if (!isYolo && isSensitive && activeApprovalHandler != null)
                    {
                        approved = await activeApprovalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        if (approved != true)
                        {
                            await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Denied");
                            return new ToolUseResult { ToolUseId = request.Id, Content = "User denied permission.", IsError = true };
                        }
                    }
                }
                // safetyResult == SafeSystem or NotApplicable is allowed for internal agent functions (Skills/db)

                var result = await tool.ExecuteAsync(jsonInput, context, ct);
                
                // Only log sensitive or non-applicable safety results to keep logs clean but useful
                if (isSensitive || safetyResult != PathSafetyResult.NotApplicable)
                {
                    await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Success");
                }

                return new ToolUseResult { ToolUseId = request.Id, Content = result, IsError = false };
            }
            catch (OperationCanceledException)
            {
                await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Cancelled");
                return new ToolUseResult { ToolUseId = request.Id, Content = "Execution Cancelled by User.", IsError = true };
            }
            catch (Exception ex)
            {
                await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, $"Error: {ex.Message}");
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Execution Error: {ex.Message}", IsError = true };
            }
        }

        private async Task LogAuditAsync(string toolName, string input, PathSafetyResult safety, bool? approved, string status)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("audit_logs")) return null!;
                var df = u.GetTableOrThrow("audit_logs");
                
                // Sanitize input for logging (mask secrets)
                var maskedInput = SourceGuard.Filter(input).FilteredText;

                var newRowCols = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }),
                    ["User"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { Environment.UserName }),
                    ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { toolName }),
                    ["Input"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { maskedInput }),
                    ["SafetyResult"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { safety.ToString() }),
                    ["Approved"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { approved?.ToString() ?? "N/A" }),
                    ["Status"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { status })
                };

                var newRowDf = new DataFrame(newRowCols);
                var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                u.AddOrUpdateTable("audit_logs", updatedDf);
                
                return null!;
            });
        }

        private bool IsSensitiveTool(string name)
        {
            var sensitivePrefixes = new[] { "bash", "write", "edit", "delete", "shell", "sh", "sensitive" };
            return sensitivePrefixes.Any(p => name.ToLower().Contains(p));
        }

        public async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
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
                var concurrentTasks = concurrentRequests.Select(req => ExecuteToolAsync(req, context, overrideHandler, ct));
                var concurrentResults = await Task.WhenAll(concurrentTasks);
                results.AddRange(concurrentResults);
            }

            // Execute others sequentially
            foreach (var req in sequentialRequests)
            {
                if (ct.IsCancellationRequested) break;
                var result = await ExecuteToolAsync(req, context, overrideHandler, ct);
                results.Add(result);
            }

            return results;
        }
    }
}
