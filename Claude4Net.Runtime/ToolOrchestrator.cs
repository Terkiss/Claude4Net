using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    public class ToolOrchestrator : IToolRegistry
    {
        private readonly List<ITool> _coreTools;
        private readonly List<ITool> _dynamicTools = new();
        private readonly IUserApprovalHandler? _approvalHandler;
        private readonly IServiceProvider _serviceProvider;
        private readonly PermissionEnforcer _permissionEnforcer = new();
        private readonly CommandRiskClassifier _commandRiskClassifier = new();

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
                    var assembly = Assembly.Load(rawAssembly);
                    var toolTypes = assembly.GetTypes()
                        .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                    foreach (var type in toolTypes)
                    {
                        var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type) as ITool;
                        if (instance != null) _dynamicTools.Add(instance);
                    }
                }
                catch
                {
                    // Ignore invalid plugin DLLs and keep the host available.
                }
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

        public virtual async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
        {
            var tool = GetTool(request.Name);
            if (tool == null)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };
            }

            string jsonInput = JsonSerializer.Serialize(request.Input);

            // --- K033: HookPipeline Integration (Before Execution) ---
            var hookPipeline = _serviceProvider?.GetService<HookPipeline>();
            var hookCtx = new HookContext { ToolName = tool.Name, Arguments = jsonInput, SessionId = AppState.SessionId };
            if (hookPipeline != null)
            {
                var beforeResult = await hookPipeline.ExecuteBeforeAsync(hookCtx);
                if (beforeResult?.ShouldAbort == true)
                {
                    await LogAuditAsync(tool.Name, jsonInput, PathSafetyResult.NotApplicable, null, $"Aborted by Hook: {beforeResult.AbortReason}");
                    return new ToolUseResult { ToolUseId = request.Id, Content = $"Execution aborted by hook: {beforeResult.AbortReason}", IsError = true };
                }
            }
            var safetyResult = new PathSafetyEvaluator().EvaluateInputSafety(request.Input);
            bool isSensitive = IsSensitiveTool(tool.Name);
            var commandRisk = _commandRiskClassifier.ClassifyFromToolInput(tool.Name, request.Input);
            var permission = _permissionEnforcer.Evaluate(
                AppState.CurrentPermissionMode,
                tool.Name,
                safetyResult,
                isSensitive,
                commandRisk);
            var activeApprovalHandler = overrideHandler ?? _approvalHandler;
            bool? approved = null;

            try
            {
                if (permission.Decision == PermissionDecision.Deny)
                {
                    await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, $"Forbidden: {permission.Reason}");
                    return new ToolUseResult { ToolUseId = request.Id, Content = $"Security Error: {permission.Reason}.", IsError = true };
                }

                if (permission.Decision == PermissionDecision.RequireApproval)
                {
                    if (activeApprovalHandler == null)
                    {
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, $"Denied (No Handler): {permission.Reason}");
                        return new ToolUseResult { ToolUseId = request.Id, Content = $"Security Error: {permission.Reason}, but no approval handler is available.", IsError = true };
                    }
                    else
                    {
                        if (safetyResult == PathSafetyResult.Outside)
                        {
                            AnsiConsole.MarkupLine("[bold red]SECURITY ALERT: outside workspace access requested.[/]");
                        }

                        // K017: Diff Approval Workflow Integration
                        if (tool is IPreviewableTool previewTool && activeApprovalHandler is IRichApprovalHandler richHandler)
                        {
                            var diff = await previewTool.GetPreviewAsync(jsonInput);
                            if (diff != null)
                            {
                                approved = await richHandler.RequestApprovalWithDiffAsync(tool.Name, jsonInput, diff);
                            }
                            else
                            {
                                approved = await activeApprovalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                            }
                        }
                        else
                        {
                            approved = await activeApprovalHandler.RequestApprovalAsync(tool.Name, jsonInput);
                        }

                        if (approved != true)
                        {
                            string status = approved == false ? "Denied" : "Cancelled/TimedOut";
                            await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, $"{status}: {permission.Reason}");
                            string denial = safetyResult == PathSafetyResult.Outside
                                ? "User denied outside-access. Security policy enforced."
                                : "User denied permission.";
                            return new ToolUseResult { ToolUseId = request.Id, Content = denial, IsError = true };
                        }
                    }
                }

                if (safetyResult == PathSafetyResult.Workspace && string.IsNullOrEmpty(AppState.CurrentCwd))
                {
                    await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Error (Workspace Not Set)");
                    return new ToolUseResult { ToolUseId = request.Id, Content = "Error: Workspace not set. Use /setworkspace <path> first.", IsError = true };
                }

                // --- K029: Automatic Checkpointing before file-modifying or memory-modifying tools ---
                bool isFileModifying = IsFileModifyingTool(tool.Name);
                bool isMemoryModifying = IsMemoryModifyingTool(tool.Name);

                if (!string.IsNullOrEmpty(AppState.CurrentCwd) && (isFileModifying || isMemoryModifying))
                {
                    try
                    {
                        var checkpointStore = new CheckpointStore(AppState.CurrentCwd, AppState.SessionId);
                        var targetFiles = ExtractTargetFiles(tool.Name, request.Input);
                        if (targetFiles.Any() || isMemoryModifying)
                        {
                            string cpId = await checkpointStore.CreateCheckpointAsync(request.Id, tool.Name, targetFiles, includeMemoryState: isMemoryModifying);
                            AnsiConsole.MarkupLine($"[grey]Auto-checkpoint created: {cpId} (pre-{tool.Name})[/]");

                            if (tool is IPreviewableTool previewTool)
                            {
                                var diff = await previewTool.GetPreviewAsync(jsonInput);
                                if (diff != null && !string.IsNullOrEmpty(diff.DiffContent))
                                {
                                    await checkpointStore.SaveDiffAsync(cpId, diff.DiffContent);
                                }
                            }
                        }
                    }
                    catch (Exception cpEx)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning: Checkpoint failed: {cpEx.Message}[/]");
                    }
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await tool.ExecuteAsync(jsonInput, context, ct);
                sw.Stop();

                // --- K033: HookPipeline Integration (After Execution) ---
                if (hookPipeline != null)
                {
                    hookCtx.Result = result?.ToString();
                    hookCtx.ElapsedMs = sw.ElapsedMilliseconds;
                    await hookPipeline.ExecuteAfterAsync(hookCtx);
                }

                // --- K035: AuditTrailService Integration (Success) ---
                var auditService = _serviceProvider?.GetService<AuditTrailService>();
                if (auditService != null)
                {
                    auditService.Record(new AuditEntry
                    {
                        Action = tool.Name,
                        Category = AuditCategory.ToolExecution,
                        Outcome = "Success",
                        Severity = AuditSeverity.Info,
                        SessionId = AppState.SessionId,
                        Metadata = new Dictionary<string, string> { ["ElapsedMs"] = sw.ElapsedMilliseconds.ToString() }
                    });
                }

                if (isSensitive || safetyResult != PathSafetyResult.NotApplicable || commandRisk.RequiresApproval)
                {
                    await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Success");
                }

                return new ToolUseResult { ToolUseId = request.Id, Content = result, IsError = false };
            }
            catch (OperationCanceledException)
            {
                await LogAuditAsync(tool.Name, jsonInput, PathSafetyResult.NotApplicable, approved, "Cancelled");
                return new ToolUseResult { ToolUseId = request.Id, Content = "Execution Cancelled by User.", IsError = true };
            }
            catch (Exception ex)
            {
                // --- K033: HookPipeline Integration (Error) ---
                if (hookPipeline != null)
                {
                    hookCtx.IsError = true;
                    hookCtx.Result = ex.Message;
                    await hookPipeline.ExecuteOnErrorAsync(hookCtx);
                }

                // --- K035: AuditTrailService Integration (Error) ---
                var auditService = _serviceProvider?.GetService<AuditTrailService>();
                auditService?.Record(new AuditEntry
                {
                    Action = tool.Name,
                    Category = AuditCategory.ToolExecution,
                    Outcome = $"Error: {ex.Message}",
                    Severity = AuditSeverity.Critical,
                    SessionId = AppState.SessionId
                });

                await LogAuditAsync(tool?.Name ?? request.Name, jsonInput, PathSafetyResult.NotApplicable, approved, $"Error: {ex.Message}");
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Execution Error: {ex.Message}", IsError = true };
            }
        }

        private async Task LogAuditAsync(string toolName, string input, PathSafetyResult safety, bool? approved, string status)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("audit_logs")) return null!;
                var df = u.GetTableOrThrow("audit_logs");
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

        private static bool IsSensitiveTool(string name)
        {
            var sensitivePrefixes = new[] { "bash", "write", "edit", "delete", "shell", "sh", "sensitive" };
            return sensitivePrefixes.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsFileModifyingTool(string name)
        {
            var modifiers = new[] { "write", "edit", "replace", "sed", "patch", "delete", "remove", "save" };
            return modifiers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMemoryModifyingTool(string name)
        {
            var memoryModifiers = new[] { "pandas_agent_memory_upsert", "pandas_agent_memory_clear", "pandas_restore", "pandas_import" };
            return memoryModifiers.Any(m => name.Equals(m, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> ExtractTargetFiles(string toolName, object? input)
        {
            var files = new List<string>();
            if (input == null) return files;

            try
            {
                var json = JsonSerializer.Serialize(input);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // common patterns: file_path, path, file, target
                string[] keys = { "file_path", "path", "file", "target" };
                foreach (var key in keys)
                {
                    if (root.TryGetProperty(key, out var prop))
                    {
                        files.Add(prop.GetString() ?? "");
                    }
                }
            }
            catch { }
            return files.Where(f => !string.IsNullOrEmpty(f)).ToList();
        }

        public virtual async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
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

            if (concurrentRequests.Any())
            {
                var concurrentTasks = concurrentRequests.Select(req => ExecuteToolAsync(req, context, overrideHandler, ct));
                results.AddRange(await Task.WhenAll(concurrentTasks));
            }

            foreach (var req in sequentialRequests)
            {
                if (ct.IsCancellationRequested) break;
                results.Add(await ExecuteToolAsync(req, context, overrideHandler, ct));
            }

            return results;
        }
    }

    /// <summary>
    /// Thread-safe and Idempotent Engine to handle tool execution approvals.
    /// </summary>
    public static class IdempotentApprovalEngine
    {
        private static readonly object _lock = new();
        private static readonly Dictionary<string, ApprovalDecisionState> _decisions = new();
        private static readonly List<Action<string, bool, string?>> _resolvers = new();

        public class ApprovalDecisionState
        {
            public string RequestId { get; set; } = "";
            public bool? Approved { get; set; }
            public string? Reason { get; set; }
            public string? Tool { get; set; }
        }

        public static void RegisterRequest(string requestId, string? tool = null)
        {
            lock (_lock)
            {
                if (!_decisions.ContainsKey(requestId))
                {
                    _decisions[requestId] = new ApprovalDecisionState
                    {
                        RequestId = requestId,
                        Approved = null,
                        Tool = tool
                    };
                }
            }
        }

        public static bool TryRegisterDecision(string requestId, bool approved, string? reason, out string? errorMsg)
        {
            errorMsg = null;
            lock (_lock)
            {
                if (!_decisions.TryGetValue(requestId, out var state))
                {
                    state = new ApprovalDecisionState
                    {
                        RequestId = requestId,
                        Approved = approved,
                        Reason = reason
                    };
                    _decisions[requestId] = state;
                    TriggerResolvers(requestId, approved, reason);
                    return true;
                }

                if (state.Approved.HasValue)
                {
                    if (state.Approved.Value == approved)
                    {
                        return true;
                    }
                    else
                    {
                        errorMsg = $"Conflicting decision for request {requestId}: already {(state.Approved.Value ? "Approved" : "Rejected")}, attempted to {(approved ? "Approve" : "Reject")}.";
                        return false;
                    }
                }

                state.Approved = approved;
                state.Reason = reason;

                TriggerResolvers(requestId, approved, reason);
                return true;
            }
        }

        public static bool? GetDecision(string requestId)
        {
            lock (_lock)
            {
                if (_decisions.TryGetValue(requestId, out var state))
                {
                    return state.Approved;
                }
                return null;
            }
        }

        public static void RegisterResolver(Action<string, bool, string?> resolver)
        {
            lock (_lock)
            {
                _resolvers.Add(resolver);
            }
        }

        public static void UnregisterResolver(Action<string, bool, string?> resolver)
        {
            lock (_lock)
            {
                _resolvers.Remove(resolver);
            }
        }

        private static void TriggerResolvers(string requestId, bool approved, string? reason)
        {
            var resolversCopy = _resolvers.ToList();
            foreach (var resolver in resolversCopy)
            {
                try
                {
                    resolver(requestId, approved, reason);
                }
                catch
                {
                    // ignore
                }
            }
        }

        public static void Reset()
        {
            lock (_lock)
            {
                _decisions.Clear();
                _resolvers.Clear();
            }
        }
    }
}
