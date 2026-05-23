using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Linq;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K060RoutineSchedulerTests : IAsyncDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;
        private readonly RoutineSchedulerService _scheduler;

        public K060RoutineSchedulerTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Scheduler_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
            _store = new RoutineStore(_workspace);
            _runner = new RoutineRunner(_store, new PermissionEnforcer(), new PathSafetyEvaluator());

            _scheduler = new RoutineSchedulerService(_store, _runner, _workspace);
            _scheduler.MinimumIntervalFloor = TimeSpan.FromSeconds(1);
        }

        public async ValueTask DisposeAsync()
        {
            await _scheduler.DisposeAsync();
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public void StartAndStop_ShouldNotThrow()
        {
            _scheduler.Start();
        }

        [Fact]
        public async Task Scheduler_ShouldRunDueRoutine_AndSkipDisabled()
        {
            // disabled routine
            var r1 = new RoutineDefinition
            {
                Id = "r1",
                Enabled = false,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" }
            };

            // due routine
            var r2 = new RoutineDefinition
            {
                Id = "r2",
                Enabled = true,
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "00:00:01" }
            };

            await _store.SaveAsync(r1);
            await _store.SaveAsync(r2);

            _scheduler.Start();

            // Wait for interval
            await Task.Delay(2000);

            var r1Records = _store.GetRunRecords("r1").ToList();
            var r2Records = _store.GetRunRecords("r2").ToList();

            Assert.Empty(r1Records);
            Assert.NotEmpty(r2Records);
        }
    }
}
