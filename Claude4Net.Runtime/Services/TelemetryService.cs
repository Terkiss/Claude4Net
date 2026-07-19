using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Spectre.Console;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime.Services
{
    public class TelemetryService
    {
        public async Task<string> GenerateReflectionSummaryAsync()
        {
            return await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return "";
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return "";

                int totalCount = df.RowCount;

                var toolNames = new List<string>();
                var isErrors = new List<bool>();
                var errorReasons = new List<string>();
                var categories = new List<string>();

                for (int i = 0; i < df.RowCount; i++)
                {
                    toolNames.Add(df["ToolName"].GetValue(i)?.ToString() ?? "");
                    isErrors.Add(df["IsError"].GetValue(i)?.ToString() == "True");
                    errorReasons.Add(df["ErrorReason"].GetValue(i)?.ToString() ?? "");
                    categories.Add(df.Columns.Any(c => c == "Category") ? df["Category"].GetValue(i)?.ToString() ?? "Unknown" : "Unknown");
                }

                var stats = toolNames.Distinct().Select(tn =>
                {
                    var indices = toolNames.Select((n, idx) => (n, idx)).Where(x => x.n == tn).Select(x => x.idx).ToList();
                    int total = indices.Count;
                    int fails = indices.Count(idx => isErrors[idx]);
                    return new { ToolName = tn, Total = total, Fails = fails, Rate = total > 0 ? (double)fails / total : 0 };
                }).OrderByDescending(x => x.Rate).ThenByDescending(x => x.Fails).ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== 지능형 통계 진단 보고(DataUniverse Agent Trajectories) ===");
                sb.AppendLine($"전체 도구 호출횟수: {totalCount}");
                foreach (var s in stats)
                {
                    sb.AppendLine($"- {s.ToolName} : {s.Total}회 시도, {s.Fails}회 실패 (실패율 {s.Rate * 100:0.1}%)");
                }

                var failCategories = categories.Where(c => c != "Success" && c != "Unknown")
                                               .GroupBy(c => c)
                                               .OrderByDescending(g => g.Count());

                if (failCategories.Any())
                {
                    sb.AppendLine("\n실패 카테고리 분포:");
                    foreach (var c in failCategories) sb.AppendLine($" - {c.Key}: {c.Count()}회");
                }

                var topErrors = errorReasons.Where(e => !string.IsNullOrWhiteSpace(e) && e.Length > 3)
                                            .GroupBy(e => e)
                                            .OrderByDescending(g => g.Count())
                                            .Take(3);

                if (topErrors.Any())
                {
                    sb.AppendLine("\n주요 발생 오류 내용 (Top 3):");
                    foreach (var e in topErrors) sb.AppendLine($" - [{e.Count()}회 발생] {e.Key.Replace("\n", " ").Substring(0, Math.Min(150, e.Key.Length))}");
                }
                return sb.ToString();
            });
        }

        public void LogTrajectories(string sessionId, List<ToolUseRequest> toolCalls, List<ToolUseResult> batchResults)
        {
            if (batchResults.Count == 0) return;

            var telemetryList = new List<string>();
            string timestamp = DateTime.Now.ToString("O");
            foreach (var result in batchResults)
            {
                string toolName = toolCalls.FirstOrDefault(t => t.Id == result.ToolUseId)?.Name ?? "unknown_tool";
                string errorText = result.IsError ? (result.Content?.ToString() ?? "Error") : "";
                string category = result.IsError ? ErrorClassifier.Classify(toolName, errorText).ToString() : "Success";

                var dict = new Dictionary<string, object>
                {
                    { "Timestamp", timestamp },
                    { "AgentId", sessionId },
                    { "ToolName", toolName },
                    { "IsError", result.IsError },
                    { "ErrorReason", errorText },
                    { "Category", category },
                    { "Payload", result.Content?.ToString() ?? "" }
                };
                telemetryList.Add(JsonSerializer.Serialize(dict));
            }

            var jsonArrayStr = "[" + string.Join(",", telemetryList) + "]";
            _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                File.WriteAllText(tmpFile, jsonArrayStr);
                try
                {
                    var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                    if (u.ContainsTable("agent_trajectories"))
                    {
                        var df = u.GetTableOrThrow("agent_trajectories");
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("agent_trajectories", updatedDf);
                    }
                    else
                    {
                        u.AddTable("agent_trajectories", newRowDf, "Auto-collected AI execution trajectories for self-reflection.");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[Telemetry]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
                finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                return null!;
            });
        }
    }
}
