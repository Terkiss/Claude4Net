using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K097SchedulerV2Tests : IAsyncDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;
        private readonly RoutineSchedulerService _scheduler;

        public K097SchedulerV2Tests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_SchedulerV2_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
            _store = new RoutineStore(_workspace);
            _runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator());
            _scheduler = new RoutineSchedulerService(_store, _runner, _workspace);
        }

        public async ValueTask DisposeAsync()
        {
            await _scheduler.DisposeAsync();
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public void CronParser_ShouldParseStandardFieldsCorrectly()
        {
            // test a specific cron pattern: every 5 minutes
            var parser = new SimpleCronParser("*/5 * * * *");
            var baseTime = new DateTimeOffset(2026, 5, 26, 10, 0, 0, TimeSpan.FromHours(9));
            var next = parser.GetNextOccurrence(baseTime);
            
            // next should be 2026-05-26 10:05:00
            Assert.Equal(new DateTimeOffset(2026, 5, 26, 10, 5, 0, TimeSpan.FromHours(9)), next);
        }

        [Fact]
        public void CalculateNextRun_ShouldSupport5FieldCronExpression()
        {
            var routine = new RoutineDefinition
            {
                Id = "cron-routine",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "*/10 * * * *" }
            };

            var baseTime = new DateTimeOffset(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);
            var next = _scheduler.CalculateNextRun(routine, baseTime);
            Assert.Equal(baseTime.AddMinutes(10), next);
        }

        [Fact]
        public async Task ControlTowerState_ShouldAccumulateVisualDataAndReleaseGates()
        {
            // 1. Set up a verification result file to mock a previous verification check
            var sessionsDir = Path.Combine(_workspace, ".claude4net", "sessions", "verify-mock-session");
            Directory.CreateDirectory(sessionsDir);

            var check = new VerificationCheck
            {
                Name = "Standard Build",
                Command = "dotnet build",
                Result = VerificationVerdict.Pass,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt = DateTimeOffset.UtcNow
            };

            var result = new VerificationResult
            {
                VerifierSessionId = "verify-mock-session",
                Verdict = VerificationVerdict.Pass,
                Checks = new[] { check },
                Timestamp = DateTimeOffset.UtcNow
            };

            var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(sessionsDir, "verification-result.json"), json);

            // 2. Add an enabled routine
            var routine = new RoutineDefinition
            {
                Id = "active-routine",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:05:00" }
            };
            await _store.SaveAsync(routine);

            // 3. Query the Control Tower state
            var state = await _scheduler.GetControlTowerStateAsync();

            Assert.NotNull(state);
            Assert.True(state.ActiveSchedules.Any(s => s.RoutineId == "active-routine"));
            Assert.True(state.ReleaseGates.Any(g => g.GatewayName == "Standard Build" && g.IsPassed));
            Assert.True(state.BuildState.LastBuildSuccess);
        }

        [Fact]
        public async Task ActiveStandbySchedulerLock_ShouldPreventStandbyInstanceFromRunningRoutines()
        {
            // Start scheduler 1 (acquires lock)
            _scheduler.Start();

            // Wait for lock to be acquired
            await Task.Delay(200);

            // Instantiate scheduler 2 (in standby)
            await using var scheduler2 = new RoutineSchedulerService(_store, _runner, _workspace);
            scheduler2.Start();

            await Task.Delay(200);

            var state1 = await _scheduler.GetControlTowerStateAsync();
            var state2 = await scheduler2.GetControlTowerStateAsync();

            Assert.True(state1.IsSchedulerLocked);
            Assert.False(state2.IsSchedulerLocked); // scheduler2 could not acquire lock
        }

        [Fact]
        public async Task ReleaseAutomationLinkage_ShouldExecuteVerifyReleaseMock()
        {
            // We create a routine with an action to run verify-release.ps1
            var routine = new RoutineDefinition
            {
                Id = "release-routine",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Manual },
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "verify-release.ps1" } }
            };

            await _store.SaveAsync(routine);

            // Trigger the routine manual run
            await _scheduler.TriggerManualAsync("release-routine");

            // Verify run records and verification result were generated
            var runRecords = _store.GetRunRecords("release-routine").ToList();
            Assert.NotEmpty(runRecords);
            Assert.True(runRecords.First().Success);

            // Check if verification result was written
            var sessionsDir = Path.Combine(_workspace, ".claude4net", "sessions");
            Assert.True(Directory.Exists(sessionsDir));
            var dirs = Directory.GetDirectories(sessionsDir);
            Assert.NotEmpty(dirs);
            var resultFile = Path.Combine(dirs.First(), "verification-result.json");
            Assert.True(File.Exists(resultFile));
        }
    }
}
