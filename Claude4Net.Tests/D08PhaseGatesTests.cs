using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D08PhaseGatesTests : System.IDisposable
    {
        public D08PhaseGatesTests() { AppState.Tasks.Clear(); }
        public void Dispose() { AppState.Tasks.Clear(); }

        [Fact]
        public void CoordinatorStore_ShouldCreateTaskWithDefaultGates()
        {
            var store = CoordinatorStore.Instance;
            string id = "test-d08-1";
            var task = store.CreateTask(id, "Test Task", "Description");

            Assert.Equal(id, task.Id);
            Assert.Equal(CoordinatePhase.Planning, task.CurrentPhase);
            Assert.Contains(task.Gates, g => g.Name == "DesignDoc");
            Assert.Contains(task.Gates, g => g.Name == "ResourceCheck");
        }

        [Fact]
        public void CoordinatorStore_ShouldBlockTransitionIfGatesNotPassed()
        {
            var store = CoordinatorStore.Instance;
            string id = "test-d08-2";
            store.CreateTask(id, "Test Task", "Description");

            string res = store.TransitionPhase(id, CoordinatePhase.Execution);
            Assert.Contains("Error", res);
            Assert.Contains("Pending gates", res);
        }

        [Fact]
        public void CoordinatorStore_ShouldAllowTransitionAfterGatesPassed()
        {
            var store = CoordinatorStore.Instance;
            string id = "test-d08-3";
            var task = store.CreateTask(id, "Test Task", "Description");

            store.AddEvidence(id, "DesignDoc", "Tester", "Proof");
            store.UpdateGate(id, "DesignDoc", true);
            store.UpdateGate(id, "ResourceCheck", true);

            string res = store.TransitionPhase(id, CoordinatePhase.Execution);
            Assert.Contains("Success", res);
            Assert.Equal(CoordinatePhase.Execution, task.CurrentPhase);
            Assert.Contains(task.Gates, g => g.Name == "UnitTests");
        }

        [Fact]
        public void CoordinatorStore_ShouldBlockCompletionWithoutApproval()
        {
            var store = CoordinatorStore.Instance;
            string id = "test-d08-4";
            store.CreateTask(id, "Test Task", "Description");
            store.AddEvidence(id, "DesignDoc", "Tester", "Proof");
            store.UpdateGate(id, "DesignDoc", true);
            store.UpdateGate(id, "ResourceCheck", true);
            store.TransitionPhase(id, CoordinatePhase.Execution);

            string res = store.TransitionPhase(id, CoordinatePhase.Completed);
            Assert.Contains("Error", res);
            Assert.Contains("Approved status", res);
        }

        [Fact]
        public void CoordinatorStore_ShouldAllowCompletionAfterApproval()
        {
            var store = CoordinatorStore.Instance;
            string id = "test-d08-5";
            store.CreateTask(id, "Test Task", "Description");
            store.AddEvidence(id, "DesignDoc", "Tester", "Proof");
            store.UpdateGate(id, "DesignDoc", true);
            store.UpdateGate(id, "ResourceCheck", true);
            store.TransitionPhase(id, CoordinatePhase.Execution);
            
            store.SetReview(id, ReviewerDecision.Approved);

            string res = store.TransitionPhase(id, CoordinatePhase.Completed);
            Assert.Contains("Success", res);
        }
    }
}
