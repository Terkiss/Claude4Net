using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K074RoutineCommandTests : IDisposable
    {
        private readonly string _testWorkspace;
        private readonly IServiceProvider _serviceProvider;

        public K074RoutineCommandTests()
        {
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_RoutineCmd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspace);
            AppState.CurrentCwd = _testWorkspace;

            var services = new ServiceCollection();
            _serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            AppState.CurrentCwd = null;
            try { Directory.Delete(_testWorkspace, true); } catch { }
        }

        private async Task<string> ExecuteRoutineCommandAsync(string arguments)
        {
            var cmd = CommandRegistry.FindCommand("routine");
            Assert.NotNull(cmd);
            var result = await cmd.Handler!(arguments, _serviceProvider);
            return result;
        }

        [Fact]
        public async Task RoutineCommand_NoArgs_ReturnsUsage()
        {
            var result = await ExecuteRoutineCommandAsync("");
            Assert.Contains("Usage:", result);
            Assert.Contains("/routine list", result);
            Assert.Contains("/routine show", result);
            Assert.Contains("/routine add", result);
        }

        [Fact]
        public async Task RoutineCommand_List_Empty_ReturnsNoRoutines()
        {
            var result = await ExecuteRoutineCommandAsync("list");
            Assert.Contains("No routines found in workspace", result);
        }

        [Fact]
        public async Task RoutineCommand_Add_SucceedsAndDefaultsToDisabled()
        {
            var result = await ExecuteRoutineCommandAsync("add r1 \"Test Routine 1\"");
            Assert.Contains("created successfully", result);
            Assert.Contains("r1", result);

            // Verify store has it and it's disabled by default
            var store = new RoutineStore(_testWorkspace);
            var routine = await store.LoadAsync("r1");
            Assert.NotNull(routine);
            Assert.Equal("Test Routine 1", routine.Name);
            Assert.False(routine.IsEnabled);
            Assert.False(routine.Enabled);

            // Duplicate ID check
            var dupResult = await ExecuteRoutineCommandAsync("add r1 \"Duplicate Routine\"");
            Assert.Contains("already exists", dupResult);
        }

        [Fact]
        public async Task RoutineCommand_EnableAndDisable_Works()
        {
            // Add routine
            await ExecuteRoutineCommandAsync("add r1 \"Test Routine\"");

            // Enable it
            var enableResult = await ExecuteRoutineCommandAsync("enable r1");
            Assert.Contains("enabled", enableResult);

            var store = new RoutineStore(_testWorkspace);
            var routine = await store.LoadAsync("r1");
            Assert.NotNull(routine);
            Assert.True(routine.IsEnabled);

            // Disable it
            var disableResult = await ExecuteRoutineCommandAsync("disable r1");
            Assert.Contains("disabled", disableResult);

            routine = await store.LoadAsync("r1");
            Assert.NotNull(routine);
            Assert.False(routine.IsEnabled);
        }

        [Fact]
        public async Task RoutineCommand_Show_Works()
        {
            await ExecuteRoutineCommandAsync("add r1 \"Show Routine\"");
            var result = await ExecuteRoutineCommandAsync("show r1");
            Assert.Contains("Show Routine", result);
            Assert.Contains("Disabled", result);
            Assert.Contains("Manual", result);
        }

        [Fact]
        public async Task RoutineCommand_Delete_Works()
        {
            await ExecuteRoutineCommandAsync("add r1 \"To Delete\"");
            var deleteResult = await ExecuteRoutineCommandAsync("delete r1");
            Assert.Contains("deleted", deleteResult);

            var store = new RoutineStore(_testWorkspace);
            var routine = await store.LoadAsync("r1");
            Assert.Null(routine);
        }

        [Fact]
        public async Task RoutineCommand_Run_ChecksEnabledAndExecutes()
        {
            await ExecuteRoutineCommandAsync("add r1 \"Runnable Routine\"");

            // Running disabled routine should fail
            var resultDisabled = await ExecuteRoutineCommandAsync("run r1");
            Assert.Contains("is disabled", resultDisabled);

            // Enable and run
            await ExecuteRoutineCommandAsync("enable r1");
            var resultRun = await ExecuteRoutineCommandAsync("run r1");
            Assert.Contains("executed successfully", resultRun);

            // Verify LastRun is updated in store
            var store = new RoutineStore(_testWorkspace);
            var routine = await store.LoadAsync("r1");
            Assert.NotNull(routine);
            Assert.NotNull(routine.LastRun);
        }

        [Fact]
        public async Task RoutineCommand_IdSafety_ChecksAllSubcommands()
        {
            string[] unsafeIds = { "../bad", "..\\bad", "bad/id", "bad\\id", "bad:id", "bad*id?", "bad|id" };

            foreach (var badId in unsafeIds)
            {
                var addRes = await ExecuteRoutineCommandAsync($"add {badId} Name");
                Assert.Contains("Invalid Routine ID", addRes);

                var showRes = await ExecuteRoutineCommandAsync($"show {badId}");
                Assert.Contains("Invalid Routine ID", showRes);

                var enableRes = await ExecuteRoutineCommandAsync($"enable {badId}");
                Assert.Contains("Invalid Routine ID", enableRes);

                var disableRes = await ExecuteRoutineCommandAsync($"disable {badId}");
                Assert.Contains("Invalid Routine ID", disableRes);

                var deleteRes = await ExecuteRoutineCommandAsync($"delete {badId}");
                Assert.Contains("Invalid Routine ID", deleteRes);

                var runRes = await ExecuteRoutineCommandAsync($"run {badId}");
                Assert.Contains("Invalid Routine ID", runRes);
            }
        }

        [Fact]
        public async Task RoutineStore_DirectTraversal_ThrowsArgumentException()
        {
            var store = new RoutineStore(_testWorkspace);
            await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../traversal"));
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new RoutineDefinition { Id = "../traversal", Name = "Bad" }));
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("../traversal"));
        }
    }
}
