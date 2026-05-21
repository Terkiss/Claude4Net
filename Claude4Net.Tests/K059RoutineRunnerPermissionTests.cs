using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K059RoutineRunnerPermissionTests : IDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;

        public K059RoutineRunnerPermissionTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Routines_Perm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);

            _store = new RoutineStore(_workspace);

            var whitelist = new System.Collections.Generic.List<string> { _workspace };
            var pathSafety = new PathSafetyEvaluator();
            var permissionEnforcer = new PermissionEnforcer();

            _runner = new RoutineRunner(_store, permissionEnforcer, pathSafety);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public async Task DisabledRoutine_ShouldFail()
        {
            await _store.SaveAsync(new RoutineDefinition { Id = "r1", Enabled = false });

            var result = await _runner.RunAsync("r1", _workspace, PermissionMode.Prompt);
            Assert.False(result.Success);
            Assert.Contains("disabled", result.Error);
        }

        [Fact]
        public async Task RequiredPermissionHigherThanSession_ShouldFail()
        {
            await _store.SaveAsync(new RoutineDefinition { Id = "r1", RequiredPermissionMode = PermissionMode.Prompt });

            // Running in ReadOnly session while routine requires Prompt
            var result = await _runner.RunAsync("r1", _workspace, PermissionMode.ReadOnly);
            Assert.False(result.Success);
            Assert.Contains("ReadOnly", result.Error);
        }

        [Fact]
        public async Task OutsideWorkspaceScript_ShouldBeDenied()
        {
            var def = new RoutineDefinition { Id = "r1", RequiredPermissionMode = PermissionMode.Prompt };
            def.Actions.Add(new RoutineAction { Kind = RoutineActionKind.Script, Payload = "/etc/passwd" });
            await _store.SaveAsync(def);

            var result = await _runner.RunAsync("r1", _workspace, PermissionMode.Prompt);

            Assert.False(result.Success);
            Assert.Contains("outside", result.Error?.ToLowerInvariant());
        }
    }
}
