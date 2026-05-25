using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Claude4Net.SDK;
using Spectre.Console;
using System.Threading.Tasks;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// ?�이?�트???��? 치유(Self-Healing)�?지?�하???�비?�입?�다.
    /// ?�행 궤적 분석 결과�?바탕?�로 가?�드?�인(SELF_HEAL_GUIDE.md)???�성?�고 관리합?�다.
    /// </summary>
    public class SelfHealingService
    {
        private static readonly SelfHealingService _instance = new();

        /// <summary>
        /// SelfHealingService???????스?스?니??
        /// </summary>
        public static SelfHealingService Instance => _instance;

        private readonly string _guidePath;
        private readonly List<HealingDirective> _directives = new();
        private int _currentReflectionDepth = 0;
        private const int MaxReflectionDepth = 3;

        public static event Action<RecoveryPrescription>? OnRecoveryPrescribed;
        private RecoveryPrescription? _latestPrescription;

        private SelfHealingService()
        {
            _guidePath = Path.Combine(AppState.SystemBaseDir, "SELF_HEAL_GUIDE.md");
        }

        public int CurrentReflectionDepth => _currentReflectionDepth;

        public bool IncrementReflectionDepth()
        {
            if (_currentReflectionDepth >= MaxReflectionDepth) return false;
            _currentReflectionDepth++;
            return true;
        }

        public void ResetReflectionDepth() => _currentReflectionDepth = 0;

        /// <summary>
        /// 에이전트 궤적을 분석하여 실패 패턴을 분류합니다.
        /// </summary>
        public FailurePattern ClassifyPattern(IEnumerable<object> events)
        {
            _latestPrescription = null;
            var eventList = events.Cast<Claude4Net.SDK.Events.IAgentEvent>().ToList();
            if (eventList.Count < 3) return FailurePattern.None;

            // 1. 세분화된 에러 분류 및 처방 검사
            var lastErrorEvent = eventList.OfType<Claude4Net.SDK.Events.ToolResultEvent>().LastOrDefault(e => e.IsError);
            if (lastErrorEvent != null)
            {
                var toolName = "unknown_tool";
                var matchingCall = eventList.OfType<Claude4Net.SDK.Events.ToolCalledEvent>()
                    .LastOrDefault(c => c.ToolUseId == lastErrorEvent.ToolUseId);
                if (matchingCall != null)
                {
                    toolName = matchingCall.ToolName;
                }

                var refinedCat = ErrorClassifier.Classify(toolName, lastErrorEvent.Result);
                if (refinedCat != RefinedErrorCategory.Unknown)
                {
                    _latestPrescription = RecommendRecovery(refinedCat, toolName, lastErrorEvent.Result);
                    OnRecoveryPrescribed?.Invoke(_latestPrescription);

                    if (refinedCat == RefinedErrorCategory.JsonSchemaMismatch)
                    {
                        return FailurePattern.ToolUsageError;
                    }
                    if (refinedCat == RefinedErrorCategory.SymlinkEscapeViolation)
                    {
                        return FailurePattern.SecurityRejection;
                    }
                }
            }

            // 2. 무한 루프 감지 (동일 도구 & 동일 인자 연속 3회)
            var toolCalls = eventList.OfType<Claude4Net.SDK.Events.ToolCalledEvent>().ToList();
            for (int i = 0; i <= toolCalls.Count - 3; i++)
            {
                if (toolCalls[i].ToolName == toolCalls[i + 1].ToolName &&
                    toolCalls[i].ToolName == toolCalls[i + 2].ToolName &&
                    toolCalls[i].Arguments == toolCalls[i + 1].Arguments &&
                    toolCalls[i].Arguments == toolCalls[i + 2].Arguments)
                {
                    _latestPrescription = RecommendRecovery(RefinedErrorCategory.InfiniteLoop, toolCalls[i].ToolName, "Infinite loop detected");
                    OnRecoveryPrescribed?.Invoke(_latestPrescription);
                    return FailurePattern.InfiniteLoop;
                }
            }

            // 3. 환각 감지 (존재하지 않는 파일/경로 반복 시도)
            var failures = eventList.OfType<Claude4Net.SDK.Events.ToolResultEvent>()
                .Where(e => e.IsError && (e.Result?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                                           e.Result?.Contains("no such file", StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
            if (failures.Count >= 2)
            {
                _latestPrescription = RecommendRecovery(RefinedErrorCategory.Hallucination, "", "Hallucination of non-existent resource");
                OnRecoveryPrescribed?.Invoke(_latestPrescription);
                return FailurePattern.Hallucination;
            }

            // 4. 보안 거절 반복
            var rejections = eventList.OfType<Claude4Net.SDK.Events.ToolResultEvent>()
                .Where(e => e.IsError && e.Result?.Contains("Security Policy Violation", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (rejections.Count >= 2)
            {
                _latestPrescription = RecommendRecovery(RefinedErrorCategory.SymlinkEscapeViolation, "", "Security policy violation");
                OnRecoveryPrescribed?.Invoke(_latestPrescription);
                return FailurePattern.SecurityRejection;
            }

            return FailurePattern.None;
        }

        /// <summary>
        /// 실패 패턴에 따른 치유 지침을 생성합니다.
        /// </summary>
        public HealingDirective GenerateDirective(FailurePattern pattern)
        {
            var directive = new HealingDirective { Pattern = pattern };
            
            string baseInstruction = pattern switch
            {
                FailurePattern.InfiniteLoop => "You are stuck in a loop. Stop calling the same tool with the same arguments. Try a different approach or verify the state first.",
                FailurePattern.Hallucination => "You are attempting to access non-existent resources. Run 'ls' or 'dir' to verify file paths before access.",
                FailurePattern.SecurityRejection => "Your actions violate security policies. Refrain from accessing protected paths or performing restricted operations.",
                FailurePattern.ToolUsageError => "Tool arguments mismatch or invalid schema syntax. Double check parameters against the declaration.",
                _ => "Analyze the previous failure and adjust your strategy to avoid repeating the same mistake."
            };

            if (_latestPrescription != null)
            {
                directive.Instruction = $"{baseInstruction} Recommendation: {_latestPrescription.Recommendation} {(_latestPrescription.SuggestedPromptAdjustment ?? "")}".Trim();
            }
            else
            {
                directive.Instruction = baseInstruction;
            }

            _directives.Add(directive);
            return directive;
        }

        public string GetActiveDirectivesPrompt()
        {
            if (!_directives.Any(d => d.IsActive)) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("\n### ?�� Self-Healing Directives");
            foreach (var d in _directives.Where(d => d.IsActive).OrderByDescending(d => d.CreatedAt))
            {
                sb.AppendLine($"- [{d.Pattern}] {d.Instruction}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// ?�재 ?�성?�된 ?��? 치유 가?�드?�인 ?�스?��? 반환?�니??
        /// </summary>
        public string GetGuide()
        {
            var sb = new StringBuilder();
            if (File.Exists(_guidePath))
            {
                sb.AppendLine(File.ReadAllText(_guidePath));
            }
            else
            {
                sb.AppendLine("# SELF_HEAL_GUIDE\nNo active self-healing guidelines found yet.");
            }

            var directivesPrompt = GetActiveDirectivesPrompt();
            if (!string.IsNullOrEmpty(directivesPrompt))
            {
                sb.AppendLine(directivesPrompt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// ?�찰(Reflection) 분석 결과�?바탕?�로 가?�드?�인 ?�일???�데?�트?�니??
        /// ???�일?� 추후 LLM???�신???�류 ?�턴???�습?�고 ?�피?�는 ???�용?�니??
        /// </summary>
        /// <param name="reflectionSummary">?�이?�트 궤적 분석???�해 ?�성??진단??/param>
        public void UpdateGuide(string reflectionSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SELF_HEAL_GUIDE");
            sb.AppendLine($"> Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## Self-Reflection Analysis");
            sb.AppendLine(reflectionSummary);
            sb.AppendLine();
            sb.AppendLine("## Execution Guardrails");
            sb.AppendLine("1. **Path Safety**: Always verify directory existence before writing files.");
            sb.AppendLine("2. **Build Integrity**: Run `dotnet build` after significant code changes.");
            sb.AppendLine("3. **Retry Strategy**: If a tool fails with a 'Permission' or 'Quota' error, follow the recommended backoff.");
            sb.AppendLine("4. **Context Management**: If an error persists, use `!clear` or `reset` to refresh the agent context.");

            sb.AppendLine();
            sb.AppendLine("## Recommended Retry Policies");
            // ?�러 카테고리???�른 ?�시???�략 기술
            foreach (ErrorCategory cat in Enum.GetValues(typeof(ErrorCategory)))
            {
                if (cat == ErrorCategory.Unknown) continue;
                var policy = ErrorClassifier.GetRecommendedPolicy(cat);
                if (policy.Strategy != RetryStrategy.None)
                {
                    sb.AppendLine($"- **{cat}**: {policy.Strategy} (Max {policy.MaxRetries} retries, {policy.InitialDelayMs}ms base delay)");
                }
            }

            // 민감 ?�보 마스?????�일 ?�??
            string maskedContent = SourceGuard.MaskValue(sb.ToString());
            File.WriteAllText(_guidePath, maskedContent);
        }

        /// <summary>
        /// ?�정 기간??지???�행 궤적 ?�이?��? ??��?�여 ?�이?�베?�스 ?�기�?관리합?�다.
        /// </summary>
        /// <param name="keepDays">보�???기간(??</param>
        public async Task PruneTrajectoriesAsync(int keepDays = 7)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return null!;
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return null!;

                // ?�재 ?�간 기�? 보�? 기간??초과???�이???�터�?
                var cutoff = DateTime.Now.AddDays(-keepDays);

                var keptIndices = new List<int>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (DateTime.TryParse(df["Timestamp"].GetValue(i)?.ToString(), out var ts))
                    {
                        if (ts >= cutoff) keptIndices.Add(i);
                    }
                }

                // ??�� ?�?�이 ?�는 경우 ?�이�??�데?�트
                if (keptIndices.Count < df.RowCount)
                {
                    var prunedDf = df.Reorder(keptIndices.ToArray());
                    u.AddOrUpdateTable("agent_trajectories", prunedDf);
                    AnsiConsole.MarkupLine($"[grey]Telemetry Pruning:[/] Removed {df.RowCount - keptIndices.Count} old trajectory records.");
                }

                return null!;
            });
        }

        /// <summary>
        /// 분류된 에러와 환경을 기반으로 최적의 복구 전략(처방전)을 추천합니다.
        /// </summary>
        public RecoveryPrescription RecommendRecovery(RefinedErrorCategory category, string toolName, string error)
        {
            var prescription = new RecoveryPrescription
            {
                Category = category,
                RetryPolicy = ErrorClassifier.GetRecommendedPolicy(category)
            };

            switch (category)
            {
                case RefinedErrorCategory.JsonSchemaMismatch:
                    prescription.Recommendation = "Adjust dynamic parameters to match JSON Schema. Format your input arguments correctly.";
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: The previous call failed due to JSON Schema mismatch. Ensure you strictly conform to the expected parameters and types in your next attempt.";
                    break;
                case RefinedErrorCategory.RateLimit:
                    prescription.Recommendation = "Rate limit detected. Cool down or switch to alternative model routing.";
                    if (!string.IsNullOrEmpty(AppState.ActiveModel))
                    {
                        if (AppState.ActiveModel.Contains("pro", StringComparison.OrdinalIgnoreCase))
                            prescription.SuggestedModel = AppState.ActiveModel.Replace("pro", "flash", StringComparison.OrdinalIgnoreCase);
                        else if (AppState.ActiveModel.Contains("flash", StringComparison.OrdinalIgnoreCase))
                            prescription.SuggestedModel = AppState.ActiveModel.Replace("flash", "pro", StringComparison.OrdinalIgnoreCase);
                        else
                            prescription.SuggestedModel = "gemini-1.5-flash";
                    }
                    else
                    {
                        prescription.SuggestedModel = "gemini-1.5-flash";
                    }
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: Rate Limit hit. Back off and adjust request frequency.";
                    break;
                case RefinedErrorCategory.ContextLimitOver:
                    prescription.Recommendation = "Context limit exceeded. Compress session history or prune messages.";
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: Context limit is near or exceeded. Focus strictly on answering the prompt, avoiding verbose descriptions.";
                    break;
                case RefinedErrorCategory.SymlinkEscapeViolation:
                    prescription.Recommendation = "Symlink safety violation. Keep path access restricted within workspace boundaries.";
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: Path safety restriction triggered. You cannot access files outside the workspace directory or traverse symbolic links leading outside.";
                    break;
                case RefinedErrorCategory.PathError:
                    prescription.Recommendation = "Path not found. Verify file layout before reading/writing.";
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: The path could not be found. Execute a directory search or verify the existence of files before attempting to read/edit them.";
                    break;
                case RefinedErrorCategory.BuildError:
                    prescription.Recommendation = "Compilation error. Inspect build log and fix C# syntax/reference errors.";
                    prescription.SuggestedPromptAdjustment = "SYSTEM WARNING: Code build failed. Analyze compiler errors carefully and correct the code before running compile again.";
                    break;
                default:
                    prescription.Recommendation = $"Standard error handling for {category}. Retry as specified.";
                    break;
            }

            return prescription;
        }
    }

    /// <summary>
    /// 복구 처방전(Recovery Prescription) 클래스
    /// </summary>
    public class RecoveryPrescription
    {
        public RefinedErrorCategory Category { get; set; }
        public string Recommendation { get; set; } = string.Empty;
        public RetryPolicy? RetryPolicy { get; set; }
        public string? SuggestedModel { get; set; }
        public string? SuggestedPromptAdjustment { get; set; }
    }
}
