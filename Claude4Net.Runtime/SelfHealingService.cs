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
        /// SelfHealingService???��????�스?�스?�니??
        /// </summary>
        public static SelfHealingService Instance => _instance;

        private readonly string _guidePath;
        private readonly List<HealingDirective> _directives = new();
        private int _currentReflectionDepth = 0;
        private const int MaxReflectionDepth = 3;

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
        /// ?�이?�트 궤적??분석?�여 ?�패 ?�턴??분류?�니??
        /// </summary>
        public FailurePattern ClassifyPattern(IEnumerable<Claude4Net.SDK.Events.IAgentEvent> events)
        {
            var eventList = events.ToList();
            if (eventList.Count < 3) return FailurePattern.None;

            // 1. 무한 루프 감�? (?�일 ?�구 & ?�일 ?�자 ?�속 3??
            var toolCalls = eventList.OfType<Claude4Net.SDK.Events.ToolCalledEvent>().ToList();
            for (int i = 0; i <= toolCalls.Count - 3; i++)
            {
                if (toolCalls[i].ToolName == toolCalls[i + 1].ToolName &&
                    toolCalls[i].ToolName == toolCalls[i + 2].ToolName &&
                    toolCalls[i].Arguments == toolCalls[i + 1].Arguments &&
                    toolCalls[i].Arguments == toolCalls[i + 2].Arguments)
                {
                    return FailurePattern.InfiniteLoop;
                }
            }

            // 2. ?�각 감�? (존재?��? ?�는 ?�일/경로 반복 ?�도)
            var failures = eventList.OfType<Claude4Net.SDK.Events.ToolResultEvent>()
                .Where(e => e.IsError && (e.Result?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true ||
                                           e.Result?.Contains("no such file", StringComparison.OrdinalIgnoreCase) == true))
                .ToList();
            if (failures.Count >= 2) return FailurePattern.Hallucination;

            // 3. 보안 거절 반복
            var rejections = eventList.OfType<Claude4Net.SDK.Events.ToolResultEvent>()
                .Where(e => e.IsError && e.Result?.Contains("Security Policy Violation", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
            if (rejections.Count >= 2) return FailurePattern.SecurityRejection;

            return FailurePattern.None;
        }

        /// <summary>
        /// ?�패 ?�턴???�른 치유 지침을 ?�성?�니??
        /// </summary>
        public HealingDirective GenerateDirective(FailurePattern pattern)
        {
            var directive = new HealingDirective { Pattern = pattern };
            directive.Instruction = pattern switch
            {
                FailurePattern.InfiniteLoop => "You are stuck in a loop. Stop calling the same tool with the same arguments. Try a different approach or verify the state first.",
                FailurePattern.Hallucination => "You are attempting to access non-existent resources. Run 'ls' or 'dir' to verify file paths before access.",
                FailurePattern.SecurityRejection => "Your actions violate security policies. Refrain from accessing protected paths or performing restricted operations.",
                _ => "Analyze the previous failure and adjust your strategy to avoid repeating the same mistake."
            };

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
    }
}
