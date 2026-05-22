using Xunit;
using Claude4Net.Tools;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using TeruTeruPandas.Core;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D02MemoryTests : IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            // 각 테스트 실행 전 상태 초기화 및 큐 플러시
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        public async Task DisposeAsync()
        {
            // 테스트 종료 후 데이터 정리
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        [Fact]
        public async Task MemorySchema_ShouldBeCreated()
        {
            var manager = PandasUniverseManager.Instance;
            await manager.EnsureBaselineTablesAsync();

            await manager.ExecuteAsync(u =>
            {
                Assert.True(u.ContainsTable("agent_memory"), "agent_memory table should exist");
                Assert.True(u.ContainsTable("agent_trajectories"), "agent_trajectories table should exist");
                
                var memDf = u.GetTableOrThrow("agent_memory");
                Assert.Contains("AgentId", memDf.Columns);
                Assert.Contains("SessionId", memDf.Columns);
            });
        }

        [Fact]
        public async Task AgentMemoryUpsert_ShouldWork()
        {
            var tool = new PandasAgentMemoryUpsertTool();
            string uniqueAgentId = "test_agent_" + Guid.NewGuid().ToString("N");
            var args = JsonSerializer.Serialize(new
            {
                agentId = uniqueAgentId,
                role = "assistant",
                status = "idle",
                currentTask = "testing",
                sharedContext = "some context"
            });

            var result = await tool.ExecuteAsync(args, new object());
            if (result != null)
            {
                var json = JsonSerializer.Serialize(result);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "Error")
                    {
                        string errMsg = (doc.RootElement.TryGetProperty("error", out var errProp) ? errProp.GetString() : null) ?? "Unknown error";
                        throw new InvalidOperationException($"[DIAGNOSTIC] Upsert tool failed with: {errMsg}");
                    }
                }
            }
            
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.SqlExecute($"SELECT * FROM agent_memory WHERE AgentId = '{uniqueAgentId}'");
                Assert.Equal(1, df.RowCount);
                Assert.Equal("assistant", df["Role"].GetValue(0)?.ToString());
            });
        }

        [Fact]
        public async Task AgentMemoryClear_PolicyTests()
        {
            var upsertTool = new PandasAgentMemoryUpsertTool();
            var clearTool = new PandasAgentMemoryClearTool();
            var manager = PandasUniverseManager.Instance;

            string agentId1 = "agent1_" + Guid.NewGuid().ToString("N");
            string agentId2 = "agent2_" + Guid.NewGuid().ToString("N");

            // 0. Verify clean isolation at start
            await manager.ExecuteAsync(u =>
            {
                Assert.Equal(0, u.GetTableOrThrow("agent_memory").RowCount);
            });

            // 1. Setup: Add some data and another table
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = agentId1, status = "testing" }), new object());
            await manager.ExecuteAsync(u => 
            {
                var otherDf = new DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["col1"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "val1" })
                });
                u.AddOrUpdateTable("other_table", otherDf);
            });

            // 2. Test scope="all"
            await clearTool.ExecuteAsync(JsonSerializer.Serialize(new { scope = "all" }), new object());

            await manager.ExecuteAsync(u =>
            {
                // agent_memory should exist but be empty
                Assert.True(u.ContainsTable("agent_memory"));
                Assert.Equal(0, u.GetTableOrThrow("agent_memory").RowCount);

                // agent_trajectories should still exist
                Assert.True(u.ContainsTable("agent_trajectories"));

                // other_table should still exist (proves u.ClearAll() was not used)
                Assert.True(u.ContainsTable("other_table"), "Other tables should be preserved");
            });

            // 3. Test upsert works after clear
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = agentId2, status = "after_clear" }), new object());
            await manager.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");

                // Assert via filter of our unique agentId to prevent concurrency pollution interference
                var rowIndices = new List<int>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df["AgentId"].GetValue(i)?.ToString() == agentId2)
                    {
                        rowIndices.Add(i);
                    }
                }

                Assert.Single(rowIndices);
                int targetIdx = rowIndices[0];
                Assert.Equal("after_clear", df["Status"].GetValue(targetIdx)?.ToString());
            });
        }

        [Fact]
        public async Task SnapshotAndRestore_ShouldWorkSafely()
        {
            var snapshotTool = new PandasSnapshotTool();
            var restoreTool = new PandasRestoreTool();
            
            string snapshotName = "test_snapshot_" + Guid.NewGuid().ToString("N");
            
            // Snapshot
            await snapshotTool.ExecuteAsync(JsonSerializer.Serialize(new { snapshotName }), new object());
            
            var stateCtx = PandasUniverseManager.GetCurrentContext();
            string snapshotPath = Path.Combine(stateCtx.SnapshotsDir, $"{snapshotName}.db");
            Assert.True(File.Exists(snapshotPath), $"Snapshot file should exist at {snapshotPath}");
            
            // Restore
            var result = await restoreTool.ExecuteAsync(JsonSerializer.Serialize(new { snapshotName }), new object());
            Assert.Contains("restored", result.ToString());
            
            // Release SQLite file locks
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            
            // Give a tiny bit of time for OS to release the handle
            await Task.Delay(100);
        }

        [Fact]
        public async Task AgentMemoryClear_IsolatedFromPreviousRows()
        {
            var upsertTool = new PandasAgentMemoryUpsertTool();
            var clearTool = new PandasAgentMemoryClearTool();
            var manager = PandasUniverseManager.Instance;

            // 이미 잔재 데이터가 있는 척 추가
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = "legacy_agent_A", status = "old" }), new object());
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = "legacy_agent_B", status = "old" }), new object());

            // 큐 및 상태 강제 격리 재설정 호출
            await manager.ResetAndFlushForTestAsync();

            // schema 다시 초기화
            await manager.EnsureBaselineTablesAsync();

            // 테이블이 비어 있는지 확인
            await manager.ExecuteAsync(u =>
            {
                Assert.Equal(0, u.GetTableOrThrow("agent_memory").RowCount);
            });
        }

        [Fact]
        public async Task AgentMemoryClear_OnlyClearsAgentMemoryTable()
        {
            var manager = PandasUniverseManager.Instance;
            await manager.EnsureBaselineTablesAsync();

            // 다른 임의의 테이블 생성
            await manager.ExecuteAsync(u =>
            {
                var df = new DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Dummy"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "value" })
                });
                u.AddOrUpdateTable("agent_memory", df); // Dummy data in memory
                u.AddOrUpdateTable("preserved_table", df);
            });

            var clearTool = new PandasAgentMemoryClearTool();
            await clearTool.ExecuteAsync(JsonSerializer.Serialize(new { scope = "all" }), new object());

            await manager.ExecuteAsync(u =>
            {
                Assert.Equal(0, u.GetTableOrThrow("agent_memory").RowCount);
                Assert.True(u.ContainsTable("preserved_table"));
                Assert.Equal(1, u.GetTableOrThrow("preserved_table").RowCount);
            });
        }

        [Fact]
        public async Task AgentMemoryUpsert_UsesUniqueAgentId()
        {
            var upsertTool = new PandasAgentMemoryUpsertTool();
            string id1 = "unique_id_1_" + Guid.NewGuid().ToString("N");
            string id2 = "unique_id_2_" + Guid.NewGuid().ToString("N");

            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = id1, status = "active" }), new object());
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = id2, status = "active" }), new object());

            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                var ids = new List<string>();
                for (int i = 0; i < df.RowCount; i++)
                {
                    ids.Add(df["AgentId"].GetValue(i)?.ToString() ?? "");
                }
                Assert.Contains(id1, ids);
                Assert.Contains(id2, ids);
            });
        }

        [Fact]
        public async Task PandasUniverseManager_TestIsolation_DoesNotLeakAgentMemoryRows()
        {
            var upsertTool = new PandasAgentMemoryUpsertTool();
            string testId = "leak_test_" + Guid.NewGuid().ToString("N");
            await upsertTool.ExecuteAsync(JsonSerializer.Serialize(new { agentId = testId, sessionId = "leak_session" }), new object());

            // 격리 수행
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
            await PandasUniverseManager.Instance.EnsureBaselineTablesAsync();

            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("agent_memory");
                for (int i = 0; i < df.RowCount; i++)
                {
                    Assert.NotEqual(testId, df["AgentId"].GetValue(i)?.ToString());
                }
            });
        }

        [Fact]
        public void VectorColumnConcat_ShouldWorkAndPreserveValues()
        {
            // 1. 빈 baseline DataFrame 정의 (VectorColumn 포함)
            var baselineColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
            {
                ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(0),
                ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(0)
            };
            var baselineDf = new DataFrame(baselineColumns);

            // 2. Vector row를 담은 DataFrame 정의 (우리가 Concat 할 대상)
            var vectorColumns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
            {
                ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "agent_vec_1", "agent_vec_2" }),
                ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(new[]
                {
                    new float[] { 0.1f, 0.2f, 0.3f },
                    null // null/missing vector 값 안전하게 처리되는지 테스트
                }!)
            };
            var vectorDf = new DataFrame(vectorColumns);

            // 3. Concat 실행
            var resultDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { baselineDf, vectorDf }, 0);

            // 4. Assertions
            Assert.Equal(2, resultDf.RowCount);
            Assert.True(resultDf.Columns.Contains("Embedding"), "Result should contain Embedding column");

            var embeddingCol = resultDf["Embedding"];
            Assert.IsType<TeruTeruPandas.Core.Column.VectorColumn>(embeddingCol);

            // float[] 값 보존 확인
            var vec1 = embeddingCol.GetValue(0) as float[];
            Assert.NotNull(vec1);
            Assert.Equal(new float[] { 0.1f, 0.2f, 0.3f }, vec1);

            // null/missing vector 값 보존 확인
            var vec2 = embeddingCol.GetValue(1);
            Assert.Null(vec2);
        }
    }
}
