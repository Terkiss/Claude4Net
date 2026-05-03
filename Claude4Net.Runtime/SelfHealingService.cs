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
    /// 에이전트의 자가 치유(Self-Healing)를 지원하는 서비스입니다.
    /// 실행 궤적 분석 결과를 바탕으로 가이드라인(SELF_HEAL_GUIDE.md)을 생성하고 관리합니다.
    /// </summary>
    public class SelfHealingService
    {
        private static readonly SelfHealingService _instance = new();
        
        /// <summary>
        /// SelfHealingService의 싱글톤 인스턴스입니다.
        /// </summary>
        public static SelfHealingService Instance => _instance;

        private readonly string _guidePath;

        private SelfHealingService()
        {
            _guidePath = Path.Combine(AppState.SystemBaseDir, "SELF_HEAL_GUIDE.md");
        }

        /// <summary>
        /// 현재 활성화된 자가 치유 가이드라인 텍스트를 반환합니다.
        /// </summary>
        public string GetGuide()
        {
            if (File.Exists(_guidePath))
            {
                return File.ReadAllText(_guidePath);
            }
            return "# SELF_HEAL_GUIDE\nNo active self-healing guidelines found yet. Perform !reflect to generate insights.";
        }

        /// <summary>
        /// 성찰(Reflection) 분석 결과를 바탕으로 가이드라인 파일을 업데이트합니다.
        /// 이 파일은 추후 LLM이 자신의 오류 패턴을 학습하고 회피하는 데 사용됩니다.
        /// </summary>
        /// <param name="reflectionSummary">에이전트 궤적 분석을 통해 생성된 진단서</param>
        public void UpdateGuide(string reflectionSummary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SELF_HEAL_GUIDE");
            sb.AppendLine($"> Last Updated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("## 🧠 Self-Reflection Analysis");
            sb.AppendLine(reflectionSummary);
            sb.AppendLine();
            sb.AppendLine("## 🚨 Execution Guardrails");
            sb.AppendLine("1. **Path Safety**: Always verify directory existence before writing files.");
            sb.AppendLine("2. **Build Integrity**: Run `dotnet build` after significant code changes.");
            sb.AppendLine("3. **Retry Strategy**: If a tool fails with a 'Permission' or 'Quota' error, follow the recommended backoff.");
            sb.AppendLine("4. **Context Management**: If an error persists, use `!clear` or `reset` to refresh the agent context.");
            
            sb.AppendLine();
            sb.AppendLine("## 🔄 Recommended Retry Policies");
            // 에러 카테고리에 따른 재시도 전략 기술
            foreach (ErrorCategory cat in Enum.GetValues(typeof(ErrorCategory)))
            {
                if (cat == ErrorCategory.Unknown) continue;
                var policy = ErrorClassifier.GetRecommendedPolicy(cat);
                if (policy.Strategy != RetryStrategy.None)
                {
                    sb.AppendLine($"- **{cat}**: {policy.Strategy} (Max {policy.MaxRetries} retries, {policy.InitialDelayMs}ms base delay)");
                }
            }

            // 민감 정보 마스킹 후 파일 저장
            string maskedContent = SourceGuard.MaskValue(sb.ToString());
            File.WriteAllText(_guidePath, maskedContent);
        }

        /// <summary>
        /// 일정 기간이 지난 실행 궤적 데이터를 삭제하여 데이터베이스 크기를 관리합니다.
        /// </summary>
        /// <param name="keepDays">보관할 기간(일)</param>
        public async Task PruneTrajectoriesAsync(int keepDays = 7)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return null!;
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return null!;

                // 현재 시간 기준 보관 기간을 초과한 데이터 필터링
                var cutoff = DateTime.Now.AddDays(-keepDays);
                
                var keptIndices = new List<int>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (DateTime.TryParse(df["Timestamp"].GetValue(i)?.ToString(), out var ts))
                    {
                        if (ts >= cutoff) keptIndices.Add(i);
                    }
                }

                // 삭제 대상이 있는 경우 테이블 업데이트
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
