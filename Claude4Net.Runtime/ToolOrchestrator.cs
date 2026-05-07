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

        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
        {
            var tool = GetTool(request.Name);
            if (tool == null)
            {
                return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };
            }

            string jsonInput = JsonSerializer.Serialize(request.Input);
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
                    await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Forbidden");
                    return new ToolUseResult { ToolUseId = request.Id, Content = $"Security Error: {permission.Reason}.", IsError = true };
                }

                if (permission.Decision == PermissionDecision.RequireApproval)
                {
                    if (activeApprovalHandler == null)
                    {
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Denied (No Handler)");
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
                            await LogAuditAsync(tool.Name, jsonInput, safetyResult, approved, "Denied");
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

                var result = await tool.ExecuteAsync(jsonInput, context, ct);

                if (isSensitive || safetyResult != PathSafetyResult.NotApplicable || commandRisk.RequiresApproval)
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
}
