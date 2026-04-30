using Xunit;
using Claude4Net.Tools;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;
using TeruTeruPandas.Core;

namespace Claude4Net.Tests
{
    public class D02MemoryTests
    {
        [Fact]
        public async Task MemorySchema_ShouldBeCreated()
        {
            // Trigger manager initialization
            var manager = PandasUniverseManager.Instance;
            
            // Give it a moment to initialize baseline tables
            await Task.Delay(500);

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
            var args = JsonSerializer.Serialize(new
            {
                agentId = "test_agent",
                role = "assistant",
                status = "idle",
                currentTask = "testing",
                sharedContext = "some context"
            });

            var result = await tool.ExecuteAsync(args, new object());
            
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var df = u.SqlExecute("SELECT * FROM agent_memory WHERE AgentId = 'test_agent'");
                Assert.Equal(1, df.RowCount);
                Assert.Equal("assistant", df["Role"].GetValue(0)?.ToString());
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
            
            string snapshotPath = Path.Combine(AppState.SystemBaseDir, "db", "snapshots", $"{snapshotName}.db");
            Assert.True(File.Exists(snapshotPath), $"Snapshot file should exist at {snapshotPath}");
            
            // Restore
            var result = await restoreTool.ExecuteAsync(JsonSerializer.Serialize(new { snapshotName }), new object());
            Assert.Contains("restored", result.ToString());
            
            // Release SQLite file locks
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            
            // Give a tiny bit of time for OS to release the handle
            await Task.Delay(100);

            // Cleanup
            // try
            // {
            //     if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
            // }
            // catch { /* Ignore cleanup failures */ }
        }
    }
}
