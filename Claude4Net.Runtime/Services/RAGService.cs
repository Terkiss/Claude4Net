using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.SDK;
using Spectre.Console;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime.Services
{
    public class RAGService
    {
        private readonly IEmbeddingProvider? _embedding;
        private static readonly Regex KeywordRegex = new(@"\b\w{4,}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "this", "that", "there", "their", "where", "which", "could", "should", "would", "about", "above", "after", "again"
        };

        public RAGService(IEmbeddingProvider? embedding = null)
        {
            _embedding = embedding;
        }

        public async Task<string> RetrieveRelevantMemoriesAsync(string userPrompt)
        {
            if (_embedding == null) return "";

            var sw = System.Diagnostics.Stopwatch.StartNew();
            float[]? targetVector = null;

            targetVector = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("embedding_cache")) return null;
                var df = u.GetTableOrThrow("embedding_cache");
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df["Text"].GetValue(i)?.ToString() == userPrompt)
                    {
                        return df["Embedding"].GetValue(i) as float[];
                    }
                }
                return null;
            });

            if (targetVector == null)
            {
                try { targetVector = await _embedding.GetEmbeddingAsync(userPrompt); } catch { }

                if (targetVector != null && targetVector.Length > 0)
                {
                    var vec = targetVector;
                    _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                    {
                        if (!u.ContainsTable("embedding_cache")) return null!;
                        var df = u.GetTableOrThrow("embedding_cache");
                        var newRowCols = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                        {
                            ["Text"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { userPrompt }),
                            ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(new[] { vec }),
                            ["LastUsed"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.Now.ToString("O") })
                        };
                        var newRowDf = new DataFrame(newRowCols);
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("embedding_cache", updatedDf);
                        return null!;
                    });
                }
            }

            string result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory")) return "";
                var df = u.GetTableOrThrow("agent_memory");
                if (df.RowCount == 0) return "";

                DataFrame topMemories;

                if (targetVector != null && targetVector.Length > 0 && df.Columns.Contains("Embedding"))
                {
                    var embCol = df["Embedding"];
                    var validIndices = new List<int>();
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        if (embCol.GetValue(i) is float[] v && v.Length == targetVector.Length)
                        {
                            validIndices.Add(i);
                        }
                    }

                    if (validIndices.Count > 0)
                    {
                        var filteredDf = df.Reorder(validIndices.ToArray());
                        topMemories = filteredDf.OrderByDescendingCosineSimilarity("Embedding", targetVector).Head(3);
                    }
                    else
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }

                    var topSim = topMemories.Columns.Contains("Similarity") ? (double)(topMemories["Similarity"].GetValue(0) ?? -1.0) : -1.0;
                    if (topSim <= 0)
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                }
                else
                {
                    topMemories = SearchByKeywords(df, userPrompt);
                }

                if (topMemories.RowCount == 0) return "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\n[시스템 주의: 과거 상호작용 기록 및 현재 요청과 관련된 내용을 발견하였습니다. 참고하십시오.]");
                for (int i = 0; i < topMemories.RowCount; i++)
                {
                    sb.AppendLine($"--- 기록 (인덱스 {i}) ---");
                    sb.AppendLine($"요청: {topMemories["UserPrompt"].GetValue(i)}");
                    sb.AppendLine($"응답: {topMemories["AgentResponse"].GetValue(i)}");
                }
                sb.AppendLine("--------------------------------------------------------------------------\n");
                return sb.ToString();
            });

            sw.Stop();
            if (sw.ElapsedMilliseconds > 200)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Performance Warning:[/] RAG retrieval took {sw.ElapsedMilliseconds}ms.");
            }
            return result;
        }

        public async Task SaveInteractionAsync(string sessionId, string userPrompt, string agentResponse, float[]? vector = null)
        {
            var keywords = ExtractKeywords(userPrompt + " " + agentResponse);
            
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                var memoryRecord = new Dictionary<string, object?>
                {
                    ["AgentId"] = sessionId,
                    ["Role"] = "assistant",
                    ["Status"] = "active",
                    ["CurrentTask"] = userPrompt.Length > 100 ? userPrompt.Substring(0, 97) + "..." : userPrompt,
                    ["SharedContext"] = "",
                    ["LastUpdated"] = DateTime.Now.ToString("O"),
                    ["SessionId"] = sessionId,
                    ["Keywords"] = keywords,
                    ["UserPrompt"] = userPrompt,
                    ["AgentResponse"] = agentResponse,
                    ["Embedding"] = (vector != null && vector.Length > 0) ? vector : null
                };

                File.WriteAllText(tmpFile, "[" + System.Text.Json.JsonSerializer.Serialize(memoryRecord) + "]");
                try
                {
                    var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                    var df = u.GetTableOrThrow("agent_memory");
                    var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                    u.AddOrUpdateTable("agent_memory", updatedDf);
                }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[RAG Storage]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
                finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                return null!;
            });
        }

        public string ExtractKeywords(string text)
        {
            try
            {
                var words = KeywordRegex.Matches(text.ToLower())
                                 .Cast<Match>()
                                 .Select(m => m.Value)
                                 .Where(w => !StopWords.Contains(w))
                                 .GroupBy(w => w)
                                 .OrderByDescending(g => g.Count())
                                 .Take(10)
                                 .Select(g => g.Key);
                return string.Join(",", words);
            }
            catch { return ""; }
        }

        private DataFrame SearchByKeywords(DataFrame df, string userPrompt)
        {
            var keywordsStr = ExtractKeywords(userPrompt);
            if (string.IsNullOrEmpty(keywordsStr)) return df.Head(0);
            var currentKeywords = keywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var scored = new List<(int idx, int score)>();
            for (int i = 0; i < df.RowCount; i++)
            {
                var recordKeywordsStr = df.Columns.Contains("Keywords") ? df["Keywords"].GetValue(i)?.ToString() ?? "" : "";
                var recordKeywords = recordKeywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                int score = recordKeywords.Intersect(currentKeywords).Count();
                if (score > 0) scored.Add((i, score));
            }

            if (!scored.Any()) return df.Head(0);
            var indices = scored.OrderByDescending(x => x.score).ThenByDescending(x => x.idx).Take(3).Select(x => x.idx).ToArray();
            return df.Reorder(indices);
        }
    }
}
