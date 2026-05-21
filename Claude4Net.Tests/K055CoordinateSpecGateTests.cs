using System;
using System.Linq;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K055CoordinateSpecGateTests : IDisposable
    {
        public K055CoordinateSpecGateTests()
        {
            AppState.Tasks.Clear();
        }

        public void Dispose()
        {
            AppState.Tasks.Clear();
        }

        [Fact]
        public void SyncGatesFromSpec_ShouldAddGatesFromAcceptanceCriteria()
        {
            var store = CoordinatorStore.Instance;
            var task = store.CreateTask("T-001", "Task 1", "Desc");

            var spec = new SeedSpecRecord { Id = "S-001", Status = SeedSpecStatus.Locked };
            spec.AcceptanceCriteria.Add(new AcceptanceCriterion { Id = "AC1", Description = "Test UI", Required = true });
            spec.AcceptanceCriteria.Add(new AcceptanceCriterion { Id = "AC2", Description = "Optional API", Required = false });

            store.SyncGatesFromSpec("T-001", spec);

            Assert.Equal("S-001", task.SpecId);
            Assert.NotNull(task.SpecLockedAt);

            var gate1 = task.Gates.FirstOrDefault(g => g.Name == "Spec-AC1");
            Assert.NotNull(gate1);
            Assert.True(gate1.IsEvidenceRequired);
            Assert.False(gate1.IsPassed);

            var gate2 = task.Gates.FirstOrDefault(g => g.Name == "Spec-AC2");
            Assert.NotNull(gate2);
            Assert.False(gate2.IsEvidenceRequired);
            Assert.True(gate2.IsPassed);
        }

        [Fact]
        public void TransitionPhase_ShouldBlockExecutionIfSpecAttachedButNotLocked()
        {
            var store = CoordinatorStore.Instance;
            var task = store.CreateTask("T-002", "Task 2", "Desc");

            // Pass the default Planning gates so we can test the Spec blocking
            store.AddEvidence("T-002", "DesignDoc", "Tester", "Summary");
            store.UpdateGate("T-002", "DesignDoc", true);
            store.UpdateGate("T-002", "ResourceCheck", true);

            var spec = new SeedSpecRecord { Id = "S-002", Status = SeedSpecStatus.Draft };
            store.SyncGatesFromSpec("T-002", spec);

            var result = store.TransitionPhase("T-002", CoordinatePhase.Execution);
            Assert.Contains("Error", result);
            Assert.Contains("must be locked first", result);
            Assert.Equal(CoordinatePhase.Planning, task.CurrentPhase);
        }

        [Fact]
        public void TransitionPhase_ShouldAllowExecutionIfSpecIsLocked()
        {
            var store = CoordinatorStore.Instance;
            var task = store.CreateTask("T-003", "Task 3", "Desc");

            // Pass the default Planning gates
            store.AddEvidence("T-003", "DesignDoc", "Tester", "Summary");
            store.UpdateGate("T-003", "DesignDoc", true);
            store.UpdateGate("T-003", "ResourceCheck", true);

            var spec = new SeedSpecRecord { Id = "S-003", Status = SeedSpecStatus.Locked };
            store.SyncGatesFromSpec("T-003", spec);

            var result = store.TransitionPhase("T-003", CoordinatePhase.Execution);
            Assert.DoesNotContain("Error", result);
            Assert.Equal(CoordinatePhase.Execution, task.CurrentPhase);
        }
    }
}
