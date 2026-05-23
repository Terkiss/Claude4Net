using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K076RoutineSchedulerHardeningTests : IAsyncDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;
        private readonly RoutineSchedulerService _scheduler;

        public K076RoutineSchedulerHardeningTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Scheduler_Hardening_" + Guid.NewGuid().ToString("N"));
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
        public void CalculateNextRun_Interval_ShouldRespectFloorAndLastRun()
        {
            var routine = new RoutineDefinition
            {
                Id = "test-interval",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:02" } // 2 seconds
            };

            var baseTime = DateTimeOffset.UtcNow;
            
            // Floor is 5 seconds by default
            var next1 = _scheduler.CalculateNextRun(routine, baseTime);
            Assert.Equal(baseTime + TimeSpan.FromSeconds(5), next1);

            // Set floor to 1 second, should use expression interval (2 seconds)
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
            var next2 = _scheduler.CalculateNextRun(routine, baseTime);
            Assert.Equal(baseTime + TimeSpan.FromSeconds(2), next2);

            // With LastRun set
            var lastRun = baseTime.AddMinutes(-1);
            routine.LastRun = lastRun;
            var next3 = _scheduler.CalculateNextRun(routine, baseTime);
            Assert.Equal(lastRun + TimeSpan.FromSeconds(2), next3);
        }

        [Fact]
        public void CalculateNextRun_DailyTime_ShouldCalculateAccurately()
        {
            var routine = new RoutineDefinition
            {
                Id = "test-daily",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.DailyTime, Expression = "14:30:00" }
            };

            // baseTime: today at 12:00:00
            var baseTime = new DateTimeOffset(2026, 5, 23, 12, 0, 0, TimeSpan.FromHours(9));
            var next = _scheduler.CalculateNextRun(routine, baseTime);
            // Should be today at 14:30:00
            Assert.Equal(new DateTimeOffset(2026, 5, 23, 14, 30, 0, TimeSpan.FromHours(9)), next);

            // baseTime: today at 15:00:00
            var baseTimePast = new DateTimeOffset(2026, 5, 23, 15, 0, 0, TimeSpan.FromHours(9));
            var nextPast = _scheduler.CalculateNextRun(routine, baseTimePast);
            // Should be tomorrow at 14:30:00
            Assert.Equal(new DateTimeOffset(2026, 5, 24, 14, 30, 0, TimeSpan.FromHours(9)), nextPast);
        }

        [Fact]
        public void CalculateNextRun_ManualAndWebhook_ShouldBeNull()
        {
            var rManual = new RoutineDefinition
            {
                Id = "r-manual",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Manual }
            };
            var rWebhook = new RoutineDefinition
            {
                Id = "r-webhook",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Webhook }
            };

            var baseTime = DateTimeOffset.UtcNow;
            Assert.Null(_scheduler.CalculateNextRun(rManual, baseTime));
            Assert.Null(_scheduler.CalculateNextRun(rWebhook, baseTime));
        }

        [Fact]
        public async Task DisabledRoutine_ShouldClearNextRunAndNotRun()
        {
            var routine = new RoutineDefinition
            {
                Id = "disabled-routine",
                Enabled = false,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" },
                NextRun = DateTimeOffset.UtcNow.AddMinutes(-5),
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "Write-Output 'should not run'" } }
            };

            await _store.SaveAsync(routine);
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
            _scheduler.Start();

            await Task.Delay(1500);

            var loaded = await _store.LoadAsync("disabled-routine");
            Assert.NotNull(loaded);
            Assert.Null(loaded.NextRun);

            var records = _store.GetRunRecords("disabled-routine").ToList();
            Assert.Empty(records);
        }

        [Fact]
        public async Task WebhookAndEventTriggers_ShouldBeRejectedAndNextRunCleared()
        {
            var routine = new RoutineDefinition
            {
                Id = "webhook-routine",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Webhook, Expression = "http://localhost/webhook" },
                NextRun = DateTimeOffset.UtcNow.AddMinutes(-5),
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "Write-Output 'should not run'" } }
            };

            await _store.SaveAsync(routine);
            _scheduler.Start();

            await Task.Delay(1500);

            var loaded = await _store.LoadAsync("webhook-routine");
            Assert.NotNull(loaded);
            Assert.Null(loaded.NextRun);

            var records = _store.GetRunRecords("webhook-routine").ToList();
            Assert.Empty(records);
            
            // Manual trigger of webhook should also throw
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await _scheduler.TriggerManualAsync("webhook-routine");
            });
        }

        [Fact]
        public async Task ConcurrencyLimit_ShouldPreventDoubleRuns()
        {
            var routine = new RoutineDefinition
            {
                Id = "concurrency-routine",
                Enabled = true,
                PermissionMode = PermissionMode.DangerFullAccess,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" },
                // Use a script that sleeps to ensure it stays active
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "Start-Sleep -Seconds 3" } }
            };

            await _store.SaveAsync(routine);
            
            // Set floor to 1 second
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
            _scheduler.Start();

            // Wait and poll until the routine starts running (triggers manual block exception)
            bool triggerFailedWhileRunning = false;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(200);
                try
                {
                    await _scheduler.TriggerManualAsync("concurrency-routine");
                }
                catch (InvalidOperationException)
                {
                    triggerFailedWhileRunning = true;
                    break;
                }
            }

            Assert.True(triggerFailedWhileRunning, "Routine did not start running or double-run prevention did not throw.");

            // Wait for it to finish and write the run record
            for (int i = 0; i < 30 && !_store.GetRunRecords("concurrency-routine").Any(); i++)
            {
                await Task.Delay(200);
            }

            var records = _store.GetRunRecords("concurrency-routine").ToList();
            // Should have run exactly once (concurrency limit prevented overlapping runs)
            Assert.Single(records);
        }

        [Fact]
        public async Task Timeout_ShouldAbortExecutionGracefully()
        {
            var routine = new RoutineDefinition
            {
                Id = "timeout-routine",
                Enabled = true,
                PermissionMode = PermissionMode.DangerFullAccess,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" },
                Timeout = TimeSpan.FromSeconds(2), // 2 seconds timeout
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "Start-Sleep -Seconds 10" } }
            };

            await _store.SaveAsync(routine);
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
            _scheduler.Start();

            // Wait for scheduler to run the routine and timeout (approx 3-4 seconds total)
            await Task.Delay(4000);

            var records = _store.GetRunRecords("timeout-routine").ToList();
            Assert.NotEmpty(records);

            var lastRun = records.Last();
            Assert.False(lastRun.Success);
            Assert.Contains("canceled", lastRun.Error ?? "", StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Run_ShouldPersistLastRunAndNextRun()
        {
            var routine = new RoutineDefinition
            {
                Id = "persist-routine",
                Enabled = true,
                PermissionMode = PermissionMode.DangerFullAccess,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" },
                Actions = { new RoutineAction { Kind = RoutineActionKind.Script, Payload = "Write-Output 'hello'" } }
            };

            await _store.SaveAsync(routine);
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
            _scheduler.Start();

            // Wait for execution to finish
            await Task.Delay(2000);

            var loaded = await _store.LoadAsync("persist-routine");
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.LastRun);
            Assert.NotNull(loaded.NextRun);
            Assert.True(loaded.NextRun > loaded.LastRun);
        }
    }
}
