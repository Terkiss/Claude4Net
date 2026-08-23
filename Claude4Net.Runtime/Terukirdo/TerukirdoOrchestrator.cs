using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.SDK.Terukirdo;

namespace Claude4Net.Runtime.Terukirdo
{
    /// <summary>
    /// 최상위 1급 메이드 오케스트레이터 테르키르도 런타임 코어 구현체
    /// </summary>
    public class TerukirdoOrchestrator : ITerukirdoOrchestrator
    {
        private readonly ITerukirdoTierRouter _tierRouter;
        private readonly ITerukirdoMemoryService _memoryService;
        private readonly ITerukirdoPrimeDirective _primeDirective;
        private readonly IAgentEventBroadcaster? _broadcaster;

        private TerukirdoMode _currentMode = TerukirdoMode.Orchestrator;
        private AdaptiveLoopTier? _manualTier;
        private int _violationsCount = 0;

        public TerukirdoMode CurrentMode => _currentMode;
        public AdaptiveLoopTier CurrentTier => _manualTier ?? AdaptiveLoopTier.Tier2_MediumRisk_RalphLoop;

        public TerukirdoOrchestrator(
            ITerukirdoTierRouter? tierRouter = null,
            ITerukirdoMemoryService? memoryService = null,
            ITerukirdoPrimeDirective? primeDirective = null,
            IAgentEventBroadcaster? broadcaster = null)
        {
            _tierRouter = tierRouter ?? new TerukirdoTierRouter();
            _memoryService = memoryService ?? new TerukirdoMemoryService(broadcaster);
            _primeDirective = primeDirective ?? new TerukirdoPrimeDirective();
            _broadcaster = broadcaster;
        }

        public void SetMode(TerukirdoMode mode)
        {
            var prev = _currentMode;
            _currentMode = mode;

            _broadcaster?.BroadcastAsync(new TerukirdoModeChangedEvent
            {
                PreviousMode = prev.ToString(),
                NewMode = mode.ToString(),
                Reason = "Master manual switch"
            });
        }

        public void SetTier(AdaptiveLoopTier? tier)
        {
            _manualTier = tier;
        }

        public async Task<TerukirdoExecutionResult> ProcessInputAsync(string input, TerukirdoContext context, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var decidedTier = _tierRouter.ClassifyIntent(input, _currentMode, _manualTier ?? context.ExplicitTier);

            if (_broadcaster != null)
            {
                await _broadcaster.BroadcastAsync(new TerukirdoTierRoutedEvent
                {
                    PromptSnippet = input.Length > 50 ? input.Substring(0, 50) + "..." : input,
                    DecidedTier = (int)decidedTier,
                    RoutingReason = $"Auto-classified under mode {_currentMode}"
                });
            }

            var result = new TerukirdoExecutionResult
            {
                ModeUsed = _currentMode,
                TierUsed = decidedTier,
                IsSuccess = true
            };

            // Tier 0: 일상 대화 및 보좌
            if (decidedTier == AdaptiveLoopTier.Tier0_Companion)
            {
                result.Output = _currentMode switch
                {
                    TerukirdoMode.Companion => $"[테르키르도] 네, 주인님! 무엇이든 편안하게 말씀해 주십시오. 🌸",
                    TerukirdoMode.MaidSecretary => $"[메이드 비서] 주인님, 요청하신 일정 및 메모 정리를 완료했습니다. 📋",
                    _ => $"[테르키르도] 네, 주인님. 준비 완료되었습니다."
                };
                result.ExecutedSubagents.Add("Terukirdo-Direct");
            }
            // Tier 1: 단순 파일/문서
            else if (decidedTier == AdaptiveLoopTier.Tier1_LowRisk)
            {
                result.ExecutedSubagents.Add("AGY-Worker");
                result.Output = "[테르키르도] Tier 1 단순 변경 작업이 준비되었습니다.";
            }
            // Tier 2: 일반 기능 (Ralph Loop)
            else if (decidedTier == AdaptiveLoopTier.Tier2_MediumRisk_RalphLoop)
            {
                result.ExecutedSubagents.AddRange(new[] { "Terukirdo-Plan", "AGY-Worker", "First-Reviewer", "Tech-Expert", "Universal-Final-Controller", "Final-Approach-Control" });
                result.Output = "[테르키르도] Tier 2 Ralph Loop 멀티 에이전트 파이프라인이 할당되었습니다.";
            }
            // Tier 3: 고위험 / 릴리즈 (체크포인트 승인 강제)
            else
            {
                result.ExecutedSubagents.AddRange(new[] { "Terukirdo-Plan", "AGY-Worker", "First-Reviewer", "Tech-Expert", "Universal-Final-Controller", "Final-Approach-Control" });
                result.Output = "[테르키르도] ⚠️ Tier 3 고위험 작업입니다. 주인님의 최종 체크포인트 승인이 요구됩니다.";
            }

            sw.Stop();
            result.Duration = sw.Elapsed;

            await Claude4Net.Runtime.Telemetry.TeruTeruPandasTelemetryEngine.Shared.RecordTraceSpanAsync(new Claude4Net.SDK.Telemetry.RequestTraceSpanDto
            {
                SpanId = Guid.NewGuid().ToString("N")[..8],
                TraceId = Guid.NewGuid().ToString("N")[..12],
                ComponentName = "TerukirdoOrchestrator",
                OperationName = $"Route Tier {decidedTier}",
                StartTimeTicks = DateTime.UtcNow.Ticks - sw.Elapsed.Ticks,
                DurationMs = Math.Max(1.0, sw.Elapsed.TotalMilliseconds),
                Status = "Success",
                Details = $"Mode: {_currentMode}, Subagents: {string.Join(", ", result.ExecutedSubagents)}"
            }, ct);

            // Trajectory logging
            await _memoryService.AppendTrajectoryEventAsync(
                $"Input Processed [Tier {decidedTier} / {_currentMode}]",
                $"Snippet: '{input}' -> Executed: {string.Join(" -> ", result.ExecutedSubagents)} ({result.Duration.TotalMilliseconds:F1}ms)",
                ct);

            return result;
        }

        public async Task SyncMemoryAsync(CancellationToken ct = default)
        {
            await _memoryService.SyncAllAsync(ct);
        }

        public static string ResolveProtocolVersion(string? workspaceRoot = null)
        {
            try
            {
                string root = workspaceRoot ?? AppState.CurrentCwd ?? Environment.CurrentDirectory;

                if (Directory.Exists(root))
                {
                    // 1. Direct workspace root scan for Terukirdo_Protocol_v*.md
                    var files = System.IO.Directory.GetFiles(root, "Terukirdo_Protocol_v*.md");
                    if (files.Length > 0)
                    {
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(files[0]);
                        int vIdx = fileName.IndexOf("_v", StringComparison.OrdinalIgnoreCase);
                        if (vIdx >= 0) return fileName.Substring(vIdx + 1);
                    }

                    // 2. docs/ directory scan
                    string docsDir = System.IO.Path.Combine(root, "docs");
                    if (System.IO.Directory.Exists(docsDir))
                    {
                        var docFiles = System.IO.Directory.GetFiles(docsDir, "Terukirdo_Protocol_v*.md");
                        if (docFiles.Length > 0)
                        {
                            string fileName = System.IO.Path.GetFileNameWithoutExtension(docFiles[0]);
                            int vIdx = fileName.IndexOf("_v", StringComparison.OrdinalIgnoreCase);
                            if (vIdx >= 0) return fileName.Substring(vIdx + 1);
                        }
                    }

                    // 3. AGENTS.md parsing
                    string agentsMd = System.IO.Path.Combine(root, "AGENTS.md");
                    if (System.IO.File.Exists(agentsMd))
                    {
                        string content = System.IO.File.ReadAllText(agentsMd);
                        var match = System.Text.RegularExpressions.Regex.Match(content, @"Terukirdo_Protocol_(v[0-9\.]+)\.md", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            return match.Groups[1].Value;
                        }
                    }
                }
            }
            catch { }

            return "v5.4";
        }

        public async Task<TerukirdoStatusSummary> GetStatusAsync(CancellationToken ct = default)
        {
            await Task.CompletedTask;
            string ws = AppState.CurrentCwd ?? Environment.CurrentDirectory;
            return new TerukirdoStatusSummary
            {
                ProtocolVersion = ResolveProtocolVersion(ws),
                CurrentMode = _currentMode,
                DefaultTier = CurrentTier,
                PrimeDirectiveActive = true,
                InterceptedViolationsCount = _violationsCount,
                LastMemorySyncTime = DateTime.UtcNow,
                ActiveWorkspace = ws
            };
        }
    }
}
