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
    public class SelfHealingService
    {
        private static readonly SelfHealingService _instance = new();
        public static SelfHealingService Instance => _instance;

        private readonly string _guidePath;

        private SelfHealingService()
        {
            _guidePath = Path.Combine(AppState.SystemBaseDir, "SELF_HEAL_GUIDE.md");
        }

        public string GetGuide()
        {
            if (File.Exists(_guidePath))
            {
                return File.ReadAllText(_guidePath);
            }
            return "# SELF_HEAL_GUIDE\nNo active self-healing guidelines found yet. Perform !reflect to generate insights.";
        }

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
            foreach (ErrorCategory cat in Enum.GetValues(typeof(ErrorCategory)))
            {
                if (cat == ErrorCategory.Unknown) continue;
                var policy = ErrorClassifier.GetRecommendedPolicy(cat);
                if (policy.Strategy != RetryStrategy.None)
                {
                    sb.AppendLine($"- **{cat}**: {policy.Strategy} (Max {policy.MaxRetries} retries, {policy.InitialDelayMs}ms base delay)");
                }
            }

            // Mask any sensitive info just in case
            string maskedContent = SourceGuard.MaskValue(sb.ToString());
            File.WriteAllText(_guidePath, maskedContent);
        }

        public async Task PruneTrajectoriesAsync(int keepDays = 7)
        {
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return null!;
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return null!;

                // Simple pruning logic: Filter out rows older than keepDays
                // Assuming Timestamp is in ISO 8601 format
                var cutoff = DateTime.Now.AddDays(-keepDays);
                
                // In a real pandas-like environment we'd use filtering. 
                // Here we'll manually re-create the dataframe with newer rows.
                var keptIndices = new List<int>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (DateTime.TryParse(df["Timestamp"].GetValue(i)?.ToString(), out var ts))
                    {
                        if (ts >= cutoff) keptIndices.Add(i);
                    }
                }

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
