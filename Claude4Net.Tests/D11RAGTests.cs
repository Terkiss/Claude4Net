using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.Api;
using Claude4Net.Tools;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Net.Http;
using System;
using System.Threading;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D11RAGTests
    {
        [Fact]
        public async Task RAG_SemanticSearch_ShouldRetrieveTopMatches()
        {
            // Arrange
            var manager = PandasUniverseManager.Instance;
            
            // Wait for initialization
            await Task.Delay(500);

            // Seed memory with test data using Concat for proper row addition
            await manager.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                
                var prompts = new[] { "Match 1", "Match 2", "Match 3", "Match 4" };
                var responses = new[] { "Response 1", "Response 2", "Response 3", "Response 4" };
                var vectors = new[] 
                { 
                    new float[] { 1.0f, 0.0f }, 
                    new float[] { 0.0f, 1.0f },
                    null,
                    new float[] { 1.0f, 0.0f, 0.0f } // Wrong dim
                };

                var cols = new Dictionary<string, IColumn>();
                cols["AgentId"] = new StringColumn(Enumerable.Repeat("test", 4).ToArray());
                cols["Role"] = new StringColumn(Enumerable.Repeat("assistant", 4).ToArray());
                cols["Status"] = new StringColumn(Enumerable.Repeat("active", 4).ToArray());
                cols["CurrentTask"] = new StringColumn(Enumerable.Repeat("task", 4).ToArray());
                cols["SharedContext"] = new StringColumn(Enumerable.Repeat("", 4).ToArray());
                cols["LastUpdated"] = new StringColumn(Enumerable.Repeat(DateTime.Now.ToString("O"), 4).ToArray());
                cols["SessionId"] = new StringColumn(Enumerable.Repeat("s1", 4).ToArray());
                cols["Keywords"] = new StringColumn(Enumerable.Repeat("test", 4).ToArray());
                cols["UserPrompt"] = new StringColumn(prompts);
                cols["AgentResponse"] = new StringColumn(responses);
                
                var vCol = new VectorColumn(4);
                for(int i=0; i<4; i++) vCol.SetValue(i, vectors[i]);
                cols["Embedding"] = vCol;

                var newDf = new DataFrame(cols);
                u.AddOrUpdateTable("agent_memory", newDf);
            });

            // Act: Search with [1, 0]
            var targetVector = new float[] { 1.0f, 0.0f };
            
            await manager.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                
                // Test the core extension method
                var result = df.OrderByDescendingCosineSimilarity("Embedding", targetVector);

                // Assert
                Assert.True(result.RowCount >= 1);
                Assert.Equal("Match 1", result["UserPrompt"].GetValue(0)?.ToString());
                
                // Ensure Similarity column exists and top score is 1.0
                Assert.True(result.Columns.Contains("Similarity"));
                Assert.Equal(1.0, (double)result["Similarity"].GetValue(0)!, 5);
                
                // Match 2 should be at index 1 (Similarity 0.0)
                Assert.Equal("Match 2", result["UserPrompt"].GetValue(1)?.ToString());
                Assert.Equal(0.0, (double)result["Similarity"].GetValue(1)!, 5);

                // Row 3 (null) and Row 4 (wrong dim) should be at the bottom with -1.0 similarity
                double lastSim = (double)result["Similarity"].GetValue(result.RowCount - 1)!;
                Assert.Equal(-1.0, lastSim);
            });
        }

        [Fact]
        public void ExtractKeywords_ShouldWork()
        {
            string text = "TeruTeruPandas is a high-performance SIMD data library.";
            var keywords = ExtractKeywords(text);
            Assert.Contains("teruterupandas", keywords);
            Assert.Contains("performance", keywords);
        }

        private string ExtractKeywords(string text)
        {
            var words = System.Text.RegularExpressions.Regex.Matches(text.ToLower(), @"\b\w{4,}\b")
                             .Cast<System.Text.RegularExpressions.Match>()
                             .Select(m => m.Value)
                             .GroupBy(w => w)
                             .OrderByDescending(g => g.Count())
                             .Take(10)
                             .Select(g => g.Key);
            return string.Join(",", words);
        }
    }
}
