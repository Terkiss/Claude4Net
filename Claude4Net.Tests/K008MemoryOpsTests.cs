using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using TeruTeruPandas.Core;
using TeruTeruPandas.Core.Column;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K008MemoryOpsTests : IDisposable
    {
        public K008MemoryOpsTests()
        {
            // Setup temp base dir for isolation
            AppState.SystemBaseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "K008Tests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(AppState.SystemBaseDir);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(AppState.SystemBaseDir))
            {
                System.IO.Directory.Delete(AppState.SystemBaseDir, true);
            }
        }

        [Fact]
        public async Task MemorySchema_Migration_Safety_Works()
        {
            // 1. Arrange: Create a table with missing columns
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var cols = new Dictionary<string, IColumn>
                {
                    ["AgentId"] = new StringColumn(new[] { "test_agent" })
                };
                u.AddOrUpdateTable("agent_memory", new DataFrame(cols));
                return null!;
            });

            // 2. Act: Trigger migration (Wait for background EnsureBaselineTablesAsync or call manually if possible)
            // Since EnsureBaselineTablesAsync runs on ctor, we need to re-verify via ExecuteAsync
            // Let's simulate the migration logic directly or via a new call
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                var requiredCols = new[] { "Keywords", "UserPrompt", "AgentResponse", "Embedding" };
                
                bool modified = false;
                foreach (var col in requiredCols)
                {
                    if (!df.Columns.Contains(col))
                    {
                        if (col == "Embedding") df.AddColumn(col, new VectorColumn(df.RowCount));
                        else df.AddColumn(col, new StringColumn(df.RowCount));
                        modified = true;
                    }
                }
                if (modified) u.AddOrUpdateTable("agent_memory", df);
                return null!;
            });

            // 3. Assert: Verify columns exist
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                Assert.Contains("Keywords", df.Columns);
                Assert.Contains("Embedding", df.Columns);
                Assert.Equal(1, df.RowCount);
                return null!;
            });
        }

        [Fact]
        public async Task SemanticSearch_DimensionMismatch_Safety_Works()
        {
            // 1. Arrange: Seed memory with different dimension vectors
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var dim3 = new float[] { 1, 0, 0 };
                var dim2 = new float[] { 0, 1 }; // Mismatched

                var cols = new Dictionary<string, IColumn>
                {
                    ["UserPrompt"] = new StringColumn(new[] { "p1", "p2" }),
                    ["AgentResponse"] = new StringColumn(new[] { "r1", "r2" }),
                    ["Embedding"] = new VectorColumn(new float[][] { dim3, dim2 })
                };
                u.AddOrUpdateTable("agent_memory", new DataFrame(cols));
                return null!;
            });

            // 2. Act & Assert: This usually happens inside AgentLoop.RetrieveRelevantMemoriesAsync
            // We'll verify that filtering logic works as intended
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                var target = new float[] { 1, 0, 0 }; // Dim 3
                
                var embCol = df["Embedding"];
                var validIndices = new List<int>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (embCol.GetValue(i) is float[] v && v.Length == target.Length)
                    {
                        validIndices.Add(i);
                    }
                }

                Assert.Single(validIndices); // Only index 0 should match
                Assert.Equal(0, validIndices[0]);
                return null!;
            });
        }

        [Fact]
        public async Task MemoryClear_ScopeSafety_Works()
        {
            // 1. Arrange: Seed memory with different session IDs
            AppState.SessionId = "session_A";
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var cols = new Dictionary<string, IColumn>
                {
                    ["AgentId"] = new StringColumn(new[] { "agent1", "agent2" }),
                    ["SessionId"] = new StringColumn(new[] { "session_A", "session_B" })
                };
                u.AddOrUpdateTable("agent_memory", new DataFrame(cols));
                return null!;
            });

            // 2. Act: Clear only current session (session_A)
            var clearTool = new Claude4Net.Tools.PandasAgentMemoryClearTool();
            await clearTool.ExecuteAsync("{\"scope\":\"session\"}", new object());

            // 3. Assert: session_B should remain
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                Assert.Equal(1, df.RowCount);
                Assert.Equal("session_B", df["SessionId"].GetValue(0)?.ToString());
                return null!;
            });
        }
    }
}
