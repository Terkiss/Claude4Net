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
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(workspaceRoot, true); } catch { }
        }

        [Fact]
        public async Task Restore_WithMissingMemorySnapshot_ShouldThrowDescriptiveException()
        {
            string workspaceRoot = Path.Combine(Path.GetTempPath(), "test_ws_missing_" + Guid.NewGuid().ToString("N"));
            string sessionId = "test_session_missing";

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

            // Read the manifest to get Snapshot ID and path
            string manifestPath = Path.Combine(workspaceRoot, ".claude4net", "sessions", sessionId, "checkpoints", checkpointId, "manifest.json");
            string manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<SDK.CheckpointManifest>(manifestJson);
            Assert.NotNull(manifest);
            Assert.True(manifest.IncludesMemoryState);
            Assert.NotNull(manifest.StateSnapshotId);

            // Clear SQLite pools and collect GC to release file locks
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Delete the snapshot file manually
            string snapshotPath = Path.Combine(context.SnapshotsDir, $"{manifest.StateSnapshotId}.db");
            if (File.Exists(snapshotPath))
            {
                File.Delete(snapshotPath);
            }

            // Attempt to restore and assert it throws descriptive exception
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreCheckpointAsync(checkpointId));
            Assert.Contains("missing or has been deleted", ex.Message);

            // Cleanup
            await manager.GetStore(context).ResetAndFlushForTestAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(workspaceRoot, true); } catch { }
        }

        [Fact]
        public async Task Restore_WithCorruptedMemorySnapshot_ShouldThrowDescriptiveException()
        {
            string workspaceRoot = Path.Combine(Path.GetTempPath(), "test_ws_corrupt_" + Guid.NewGuid().ToString("N"));
            string sessionId = "test_session_corrupt";

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

            // Read the manifest to get Snapshot ID and path
            string manifestPath = Path.Combine(workspaceRoot, ".claude4net", "sessions", sessionId, "checkpoints", checkpointId, "manifest.json");
            string manifestJson = await File.ReadAllTextAsync(manifestPath);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<SDK.CheckpointManifest>(manifestJson);
            Assert.NotNull(manifest);
            Assert.True(manifest.IncludesMemoryState);
            Assert.NotNull(manifest.StateSnapshotId);

            // Clear SQLite pools and collect GC to release file locks
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Corrupt the snapshot file by writing invalid data
            string snapshotPath = Path.Combine(context.SnapshotsDir, $"{manifest.StateSnapshotId}.db");
            await File.WriteAllTextAsync(snapshotPath, "This is corrupted DB content");

            // Attempt to restore and assert it throws descriptive exception
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreCheckpointAsync(checkpointId));
            Assert.Contains("corrupted or failed to load", ex.Message);

            // Cleanup
            await manager.GetStore(context).ResetAndFlushForTestAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            try { Directory.Delete(workspaceRoot, true); } catch { }
        }
    }
}
