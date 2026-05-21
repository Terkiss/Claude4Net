using Xunit;
using Claude4Net.Runtime;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K064MemoryCheckpointTests
    {
        [Fact]
        public async Task Checkpoint_WithMemoryState_ShouldRestore()
        {
            string workspaceRoot = Path.Combine(Path.GetTempPath(), "test_ws_" + Guid.NewGuid().ToString("N"));
            string sessionId = "test_session";

            var store = new CheckpointStore(workspaceRoot, sessionId);
            var manager = PandasUniverseManager.Instance;
            var context = new WorkspaceStateContext { WorkspaceRoot = workspaceRoot, SessionId = sessionId };

            // Add initial data
            await manager.GetStore(context).ExecuteAsync(u =>
            {
                var df = new TeruTeruPandas.Core.DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Data"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "initial" })
                });
                u.AddOrUpdateTable("mem_test", df);
            });

            // Create checkpoint including memory state
            string checkpointId = await store.CreateCheckpointAsync("testCallId", "testTool", new List<string>(), "Test memory", includeMemoryState: true);

            // Modify data
            await manager.GetStore(context).ExecuteAsync(u =>
            {
                var df = new TeruTeruPandas.Core.DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Data"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "modified" })
                });
                u.AddOrUpdateTable("mem_test", df);
            });

            // Assert modified
            await manager.GetStore(context).ExecuteAsync(u =>
            {
                Assert.Equal("modified", u.GetTableOrThrow("mem_test")["Data"].GetValue(0)?.ToString());
            });

            // Restore
            await store.RestoreCheckpointAsync(checkpointId);

            // Assert restored
            await manager.GetStore(context).ExecuteAsync(u =>
            {
                Assert.Equal("initial", u.GetTableOrThrow("mem_test")["Data"].GetValue(0)?.ToString());
            });

            // Cleanup
            await manager.GetStore(context).ResetAndFlushForTestAsync();
            try { Directory.Delete(workspaceRoot, true); } catch { }
        }
    }
}
