using Xunit;
using Claude4Net.Runtime;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;
using TeruTeruPandas.Core;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K063WorkspaceStateIsolationTests
    {
        [Fact]
        public async Task Workspace_State_Is_Isolated()
        {
            var manager = PandasUniverseManager.Instance;

            var ctxA = new WorkspaceStateContext { WorkspaceRoot = Path.Combine(Path.GetTempPath(), "wsA_" + Guid.NewGuid().ToString("N")), SessionId = "session1" };
            var ctxB = new WorkspaceStateContext { WorkspaceRoot = Path.Combine(Path.GetTempPath(), "wsB_" + Guid.NewGuid().ToString("N")), SessionId = "session1" };

            // Ensure baseline tables
            await manager.GetStore(ctxA).ExecuteAsync(u => PandasUniverseManager.EnsureBaselineTablesInternal(u));
            await manager.GetStore(ctxB).ExecuteAsync(u => PandasUniverseManager.EnsureBaselineTablesInternal(u));

            await manager.GetStore(ctxA).ExecuteAsync(u =>
            {
                var df = new DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["TestCol"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "valA" })
                });
                u.AddOrUpdateTable("custom_table", df);
            });

            await manager.GetStore(ctxB).ExecuteAsync(u =>
            {
                var df = new DataFrame(new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["TestCol"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "valB" })
                });
                u.AddOrUpdateTable("custom_table", df);
            });

            // Assert isolation
            await manager.GetStore(ctxA).ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("custom_table");
                Assert.Equal("valA", df["TestCol"].GetValue(0)?.ToString());
            });

            await manager.GetStore(ctxB).ExecuteAsync(u =>
            {
                var df = u.GetTableOrThrow("custom_table");
                Assert.Equal("valB", df["TestCol"].GetValue(0)?.ToString());
            });

            // Cleanup
            await manager.GetStore(ctxA).ResetAndFlushForTestAsync();
            await manager.GetStore(ctxB).ResetAndFlushForTestAsync();
        }
    }
}
