using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K075RoutineExecutionIntegrationTests : IDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;
        private readonly string _sessionId;

        public K075RoutineExecutionIntegrationTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Routines_K075_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);

            _sessionId = "session-k075-" + Guid.NewGuid().ToString("N")[..8];
            AppState.CurrentCwd = _workspace;
            AppState.SessionId = _sessionId;
            AppState.CurrentPermissionMode = PermissionMode.Prompt;

            _store = new RoutineStore(_workspace);
        }

        public void Dispose()
        {
            AppState.CurrentCwd = null!;
            AppState.SessionId = null!;
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public async Task RunAsync_SlashCommandPipeline_SucceedsAndRecordsEventsAndHooks()
        {
            // Arrange
            var beforeHook = new SpyHook { Timing = HookTiming.BeforeToolExecution };
            var afterHook = new SpyHook { Timing = HookTiming.AfterToolExecution };
            var hookPipeline = new HookPipeline().Register(beforeHook).Register(afterHook);

            var services = new ServiceCollection();
            services.AddSingleton(hookPipeline);
            var serviceProvider = services.BuildServiceProvider();

            var routine = new RoutineDefinition
            {
                Id = "r1",
                Name = "Test Routine",
                Enabled = true,
                PermissionMode = PermissionMode.Prompt
            };
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.SlashCommand, Payload = "/help" });
            await _store.SaveAsync(routine);

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), serviceProvider);

            // Act
            var result = await runner.RunAsync("r1", _workspace, PermissionMode.Prompt);

            // Assert
            Assert.True(result.Success, $"Routine execution failed: {result.Error}");
            Assert.True(beforeHook.WasExecuted);
            Assert.True(afterHook.WasExecuted);

            // Verify event store integration
            var eventStore = new FileAgentEventStore(_workspace);
            var events = await eventStore.GetEventsAsync(_sessionId);
            var routineEvent = events.OfType<RoutineRunEvent>().FirstOrDefault();
            Assert.NotNull(routineEvent);
            Assert.Equal("r1", routineEvent.RoutineId);
            Assert.Equal(result.RunId, routineEvent.RunId);
            Assert.True(routineEvent.Success);
            Assert.Null(routineEvent.Error);
        }

        [Fact]
        public async Task RunAsync_BeforeHookAbort_CancelsExecutionAndTriggersOnErrorHook()
        {
            // Arrange
            var beforeHook = new SpyHook { Timing = HookTiming.BeforeToolExecution, ShouldAbortValue = true };
            var onErrorHook = new SpyHook { Timing = HookTiming.OnToolError };
            var hookPipeline = new HookPipeline().Register(beforeHook).Register(onErrorHook);

            var services = new ServiceCollection();
            services.AddSingleton(hookPipeline);
            var serviceProvider = services.BuildServiceProvider();

            var routine = new RoutineDefinition
            {
                Id = "r_abort",
                Name = "Abort Routine",
                Enabled = true,
                PermissionMode = PermissionMode.Prompt
            };
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.SlashCommand, Payload = "/help" });
            await _store.SaveAsync(routine);

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), serviceProvider);

            // Act
            var result = await runner.RunAsync("r_abort", _workspace, PermissionMode.Prompt);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("aborted by hook", result.Error?.ToLowerInvariant());
            Assert.True(beforeHook.WasExecuted);
            Assert.True(onErrorHook.WasExecuted);

            // Verify event store records failure
            var eventStore = new FileAgentEventStore(_workspace);
            var events = await eventStore.GetEventsAsync(_sessionId);
            var routineEvent = events.OfType<RoutineRunEvent>().FirstOrDefault();
            Assert.NotNull(routineEvent);
            Assert.False(routineEvent.Success);
            Assert.Contains("aborted by hook", routineEvent.Error?.ToLowerInvariant());
        }

        [Fact]
        public async Task RunAsync_OutsideWorkspaceScript_RejectsExecution()
        {
            // Arrange
            var routine = new RoutineDefinition
            {
                Id = "r_outside",
                Name = "Outside Workspace Routine",
                Enabled = true,
                PermissionMode = PermissionMode.Prompt
            };
            // Path outside workspace
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.Script, Payload = "../outside_script.sh" });
            await _store.SaveAsync(routine);

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), null);

            // Act
            var result = await runner.RunAsync("r_outside", _workspace, PermissionMode.Prompt);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("outside the workspace", result.Error?.ToLowerInvariant());

            // Verify event store contains failure
            var eventStore = new FileAgentEventStore(_workspace);
            var events = await eventStore.GetEventsAsync(_sessionId);
            var routineEvent = events.OfType<RoutineRunEvent>().FirstOrDefault();
            Assert.NotNull(routineEvent);
            Assert.False(routineEvent.Success);
            Assert.Contains("outside the workspace", routineEvent.Error?.ToLowerInvariant());
        }

        [Fact]
        public async Task RunAsync_ReadOnlyMode_BlocksWriteAndScriptActions()
        {
            // Arrange
            var routine = new RoutineDefinition
            {
                Id = "r_readonly_block",
                Name = "ReadOnly Script Routine",
                Enabled = true,
                PermissionMode = PermissionMode.ReadOnly
            };
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.Script, Payload = "echo hello" });
            await _store.SaveAsync(routine);

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), null);

            // Act: Run with session mode ReadOnly
            var result = await runner.RunAsync("r_readonly_block", _workspace, PermissionMode.ReadOnly);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("read-only", result.Error?.ToLowerInvariant());
        }

        [Fact]
        public async Task RunAsync_ReadOnlyMode_BlocksModifyingSlashCommand()
        {
            // Arrange
            var routine = new RoutineDefinition
            {
                Id = "r_readonly_cmd",
                Name = "ReadOnly Command Routine",
                Enabled = true,
                PermissionMode = PermissionMode.ReadOnly
            };
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.SlashCommand, Payload = "/checkpoint restore t1" });
            await _store.SaveAsync(routine);

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), null);

            // Act
            var result = await runner.RunAsync("r_readonly_cmd", _workspace, PermissionMode.ReadOnly);

            // Assert
            Assert.False(result.Success);
            Assert.Contains("readonly mode blocks modifying slash command", result.Error?.ToLowerInvariant());
        }

        [Fact]
        public async Task RunAsync_ModifyingAction_CreatesPreflightCheckpoint()
        {
            // Arrange
            var routine = new RoutineDefinition
            {
                Id = "r_checkpoint",
                Name = "Checkpoint Routine",
                Enabled = true,
                PermissionMode = PermissionMode.DangerFullAccess
            };
            // script action should trigger checkpoint creation
            routine.Actions.Add(new RoutineAction { Kind = RoutineActionKind.Script, Payload = "echo test" });
            await _store.SaveAsync(routine);

            // Create a dummy file in workspace to backup
            string dummyFile = Path.Combine(_workspace, "src.cs");
            await File.WriteAllTextAsync(dummyFile, "class Foo {}");

            var runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator(), null);

            // Act
            var result = await runner.RunAsync("r_checkpoint", _workspace, PermissionMode.DangerFullAccess);

            // Assert
            var checkpointStore = new CheckpointStore(_workspace, _sessionId);
            var checkpoints = await checkpointStore.ListCheckpointsAsync();
            Assert.NotEmpty(checkpoints);
            Assert.Contains("src.cs", checkpoints[0].ChangedFiles);
        }
    }

    public class SpyHook : IToolHook
    {
        public string Name => "SpyHook";
        public HookTiming Timing { get; set; }
        public int Priority => 1;
        public bool IsEnabled { get; set; } = true;
        public bool WasExecuted { get; private set; }
        public HookContext? Context { get; private set; }
        public bool ShouldAbortValue { get; set; }

        public Task<HookResult> ExecuteAsync(HookContext context)
        {
            WasExecuted = true;
            Context = context;
            if (ShouldAbortValue)
            {
                return Task.FromResult(HookResult.Abort(Name, "Aborted by spy"));
            }
            return Task.FromResult(HookResult.Ok(Name));
        }
    }
}
