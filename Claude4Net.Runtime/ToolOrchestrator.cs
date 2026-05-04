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
    /// <summary>
    /// 도구 실행의 오케스트레이터로, 도구의 등록, 보안 검사, 감사 로그 기록 및 다중 도구의 병렬/순차 실행을 관리합니다.
    /// </summary>
    public class ToolOrchestrator : IToolRegistry
    {
        private readonly List<ITool> _coreTools;
        private readonly List<ITool> _dynamicTools = new List<ITool>();
        private readonly IUserApprovalHandler? _approvalHandler;
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// ToolOrchestrator의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="coreTools">기본적으로 제공되는 핵심 도구 목록</param>
        /// <param name="approvalHandler">사용자 승인을 처리하는 핸들러</param>
        /// <param name="serviceProvider">동적 플러그인 생성을 위한 서비스 프로바이더</param>
        public ToolOrchestrator(IEnumerable<ITool> coreTools, IUserApprovalHandler? approvalHandler, IServiceProvider serviceProvider)
        {
            _coreTools = coreTools.ToList();
            _approvalHandler = approvalHandler;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 지정된 디렉토리의 DLL 파일로부터 동적 플러그인을 로드합니다. (Hot-Reload 지원)
        /// </summary>
        /// <param name="directoryPath">플러그인 DLL 파일들이 위치한 경로</param>
        public void ReloadDynamicPlugins(string directoryPath)
        {
            _dynamicTools.Clear();
            if (!Directory.Exists(directoryPath)) return;
            
            foreach (var dllPath in Directory.GetFiles(directoryPath, "*.dll"))
            {
                try
                {
                    // 메모리에 어셈블리를 직접 로드하여 파일 잠금 문제를 방지
                    byte[] rawAssembly = File.ReadAllBytes(dllPath);
                    var assembly = System.Reflection.Assembly.Load(rawAssembly);
                    var toolTypes = assembly.GetTypes().Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
                    
                    foreach(var type in toolTypes)
                    {
                        var instance = ActivatorUtilities.CreateInstance(_serviceProvider, type) as ITool;
                        if (instance != null) _dynamicTools.Add(instance);
                    }
                }
                catch { } // 비정상적인 DLL은 무시하고 계속 진행
            }
        }

        /// <summary>
        /// 새로운 도구를 핵심 도구 목록에 수동으로 추가합니다.
        /// </summary>
        public void AddTool(ITool tool)
        {
            if (!_coreTools.Any(t => t.Name == tool.Name)) _coreTools.Add(tool);
        }

        /// <summary>
        /// 현재 로드된 모든 도구(핵심+동적) 목록을 반환합니다.
        /// </summary>
        public IReadOnlyList<ITool> GetTools() => _coreTools.Concat(_dynamicTools).ToList();

        /// <summary>
        /// 도구 이름이나 별칭(Alias)으로 도구를 검색합니다.
        /// </summary>
        public ITool? GetTool(string name)
        {
            return _coreTools.Concat(_dynamicTools).FirstOrDefault(t => 
                t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || 
                (t.Aliases != null && t.Aliases.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>
        /// 단일 도구를 실행합니다. 실행 전 보안 검사 및 사용자 승인 절차를 수행합니다.
        /// </summary>
        /// <param name="request">도구 실행 요청 정보</param>
        /// <param name="context">실행 컨텍스트 데이터</param>
        /// <param name="overrideHandler">선택적인 승인 핸들러 오버라이드</param>
        /// <param name="ct">취소 토큰</param>
        public async Task<ToolUseResult> ExecuteToolAsync(ToolUseRequest request, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
        {
            var tool = GetTool(request.Name);
            if (tool == null) return new ToolUseResult { ToolUseId = request.Id, Content = $"Error: Tool '{request.Name}' not found.", IsError = true };

            string jsonInput = JsonSerializer.Serialize(request.Input);
            
            // --- 보안 검사 단계 ---
            var evaluator = new PathSafetyEvaluator();
            var safetyResult = evaluator.EvaluateInputSafety(request.Input);
            bool isYolo = AppState.CurrentPermissionMode == PermissionMode.Yolo || 
                          AppState.CurrentPermissionMode == PermissionMode.BypassPermissions;
            bool isSensitive = IsSensitiveTool(tool.Name);
            var activeApprovalHandler = overrideHandler ?? _approvalHandler;
            bool? approved = null;

            try
            {
                // 샌드박스 보안 정책 적용
                if (safetyResult == PathSafetyResult.Outside) 
                {
                    // 1. 작업 공간(Workspace) 외부 경로 접근 시도 시 처리
                    if (isYolo)
                    {
                        // YOLO 모드여도 외부 접근은 명시적 사용자 승인을 요구 (최소한의 안전장치)
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
                        // 일반 모드에서는 외부 접근을 엄격히 차단
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Forbidden");
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Security Error: Access to paths outside the workspace is strictly prohibited in Normal mode.", IsError = true };
                    }
                }
                else if (safetyResult == PathSafetyResult.Workspace) 
                {
                    // 2. 작업 공간 내부 접근 처리
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        await LogAuditAsync(tool.Name, jsonInput, safetyResult, null, "Error (Workspace Not Set)");
                        return new ToolUseResult { ToolUseId = request.Id, Content = "Error: Workspace not set. Use /setworkspace <path> first.", IsError = true };
                    }

                    // 민감한 도구(쓰기, 실행 등)인 경우 사용자 승인 요청
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

                // --- 실제 도구 실행 단계 ---
                var result = await tool.ExecuteAsync(jsonInput, context, ct);
                
                // 감사 로그 기록
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

        /// <summary>
        /// 도구 실행 내역을 인메모리 감사 로그 DB(TeruTeruPandas)에 기록합니다.
        /// </summary>
        private async Task LogAuditAsync(string toolName, string input, PathSafetyResult safety, bool? approved, string status)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("audit_logs")) return null!;
                var df = u.GetTableOrThrow("audit_logs");
                
                // 민감 정보 필터링 후 로그 기록
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

        /// <summary>
        /// 여러 도구 실행 요청을 배치로 처리합니다. 
        /// 동시 실행 가능 여부(IsConcurrencySafe)에 따라 병렬 또는 순차적으로 실행합니다.
        /// </summary>
        public async Task<List<ToolUseResult>> ExecuteBatchAsync(IEnumerable<ToolUseRequest> requests, object context, IUserApprovalHandler? overrideHandler = null, CancellationToken ct = default)
        {
            var results = new List<ToolUseResult>();
            var concurrentRequests = new List<ToolUseRequest>();
            var sequentialRequests = new List<ToolUseRequest>();

            // 도구의 속성에 따라 실행 그룹 분리
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

            // 1. 동시 실행 안전 도구들은 병렬 처리 (성능 최적화)
            if (concurrentRequests.Any())
            {
                var concurrentTasks = concurrentRequests.Select(req => ExecuteToolAsync(req, context, overrideHandler, ct));
                var concurrentResults = await Task.WhenAll(concurrentTasks);
                results.AddRange(concurrentResults);
            }

            // 2. 동시 실행 불가 도구들은 순차적 처리
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
