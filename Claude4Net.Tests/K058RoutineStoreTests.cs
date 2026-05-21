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
    public class K058RoutineStoreTests : IDisposable
    {
        private readonly string _workspace;
        private readonly RoutineStore _store;

        public K058RoutineStoreTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Routines_" + Guid.NewGuid().ToString("N"));
            _store = new RoutineStore(_workspace);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public async Task SaveAndLoad_ShouldPreserveDefinition()
        {
            var def = new RoutineDefinition
            {
                Id = "r1",
                Name = "Daily Check",
                Trigger = new RoutineTrigger { Kind = RoutineTriggerKind.Interval, Expression = "60" }
            };
            def.Actions.Add(new RoutineAction { Kind = RoutineActionKind.SlashCommand, Payload = "/doctor" });

            await _store.SaveAsync(def);

            var loaded = await _store.LoadAsync("r1");
            Assert.NotNull(loaded);
            Assert.Equal("Daily Check", loaded.Name);
            Assert.Equal(RoutineTriggerKind.Interval, loaded.Trigger.Kind);
            Assert.Single(loaded.Actions);
        }

        [Fact]
        public async Task ListRoutines_ShouldReturnAll()
        {
            await _store.SaveAsync(new RoutineDefinition { Id = "r1", Name = "One" });
            await _store.SaveAsync(new RoutineDefinition { Id = "r2", Name = "Two" });

            var list = _store.ListRoutines().ToList();
            Assert.Equal(2, list.Count);
        }

        [Fact]
        public async Task SaveRunRecord_ShouldSave()
        {
            var rec = new RoutineRunRecord
            {
                RunId = "run1",
                RoutineId = "r1",
                Success = true
            };
            await _store.SaveRunRecordAsync(rec);

            var dir = Path.Combine(_workspace, ".claude4net", "routine-runs", "r1");
            Assert.True(File.Exists(Path.Combine(dir, "run1.json")));
        }
    }
}
