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
    public class K070CoordinateSpecCommandTests : IDisposable
    {
        private readonly string _testWorkspace;
        private readonly IServiceProvider _serviceProvider;

        public K070CoordinateSpecCommandTests()
        {
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_CoordSpec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspace);
            AppState.CurrentCwd = _testWorkspace;
            AppState.Tasks.Clear();

            var services = new ServiceCollection();
            _serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            AppState.Tasks.Clear();
            AppState.CurrentCwd = null;
            try { Directory.Delete(_testWorkspace, true); } catch { }
        }

        private async Task<string> ExecuteCoordinateCommandAsync(string arguments)
        {
            var cmd = CommandRegistry.FindCommand("coordinate");
            Assert.NotNull(cmd);
            var result = await cmd.Handler!(arguments, _serviceProvider);
            return (string)result;
        }

        private async Task<string> ExecuteSpecCommandAsync(string arguments)
        {
            var cmd = CommandRegistry.FindCommand("spec");
            Assert.NotNull(cmd);
            var result = await cmd.Handler!(arguments, _serviceProvider);
            return (string)result;
        }

        [Fact]
        public async Task CoordinateStart_PathTraversalSpec_Rejected()
        {
            var result = await ExecuteCoordinateCommandAsync("start T-001 \"Task 1\" --spec ../badspec");
            Assert.Contains("Error", result);
            Assert.Contains("Invalid Spec ID", result);
        }

        [Fact]
        public async Task CoordinateStart_UnknownSpec_Rejected()
        {
            var result = await ExecuteCoordinateCommandAsync("start T-001 \"Task 1\" --spec spec-unknown");
            Assert.Contains("Error", result);
            Assert.Contains("spec-unknown' not found", result);
        }

        [Fact]
        public async Task CoordinateStart_UnlockedSpec_Rejected()
        {
            // Create a draft spec
            await ExecuteSpecCommandAsync("new spec-draft \"Draft Spec\"");
            await ExecuteSpecCommandAsync("criteria add spec-draft \"AC1\"");

            // Start coordinate task referencing draft spec
            var result = await ExecuteCoordinateCommandAsync("start T-002 \"Task 2\" --spec spec-draft");
            Assert.Contains("Error", result);
            Assert.Contains("is not in Locked status", result);
        }

        [Fact]
        public async Task CoordinateStart_HappyPath_AttachesSpecAndSyncsGates()
        {
            // Create spec, add criteria, and lock it
            await ExecuteSpecCommandAsync("new spec-locked \"Locked Spec\"");
            await ExecuteSpecCommandAsync("criteria add spec-locked \"AC Required\"");

            // Add optional criteria (Wait, our /spec criteria command defaults to required=true.
            // So we can manually modify the spec JSON to test optional AC since cmd registry criteria add defaults to required=true,
            // or just test required criteria. Let's do both by loading the spec file, modifying a criteria to not required, and saving it.)
            var specStore = new SeedSpecStore(_testWorkspace);
            var spec = await specStore.LoadAsync("spec-locked");
            Assert.NotNull(spec);

            var optionalAc = new AcceptanceCriterion { Id = "AC-Opt", Description = "AC Optional", Required = false };
            spec.AcceptanceCriteria.Add(optionalAc);
            await specStore.SaveAsync(spec);

            // Lock spec
            var lockResult = await ExecuteSpecCommandAsync("lock spec-locked");
            Assert.Contains("now Locked", lockResult);

            // Start coordinate task
            var result = await ExecuteCoordinateCommandAsync("start T-003 \"Task 3\" --spec spec-locked");
            Assert.Contains("started with ID", result);

            // Verify task
            Assert.True(AppState.Tasks.TryGetValue("T-003", out var st));
            var task = st as CoordinateTask;
            Assert.NotNull(task);
            Assert.Equal("spec-locked", task.SpecId);
            Assert.NotNull(task.SpecLockedAt);

            // Verify gates
            var gateReq = task.Gates.FirstOrDefault(g => g.Name == "Spec-AC-1");
            Assert.NotNull(gateReq);
            Assert.True(gateReq.IsEvidenceRequired);
            Assert.False(gateReq.IsPassed);

            var gateOpt = task.Gates.FirstOrDefault(g => g.Name == "Spec-AC-Opt");
            Assert.NotNull(gateOpt);
            Assert.False(gateOpt.IsEvidenceRequired);
            Assert.True(gateOpt.IsPassed);
        }

        [Fact]
        public async Task CoordinateTransition_UnansweredBlockingQuestions_BlocksExecution()
        {
            // Create spec with a blocking question, add criteria, and lock it
            await ExecuteSpecCommandAsync("new spec-blocking \"Blocking Spec\"");
            await ExecuteSpecCommandAsync("criteria add spec-blocking \"AC Required\"");
            await ExecuteSpecCommandAsync("question spec-blocking \"Blocking Question?\"");

            // Try to lock (should fail because unanswered question)
            var lockResult1 = await ExecuteSpecCommandAsync("lock spec-blocking");
            Assert.Contains("Unanswered blocking questions exist", lockResult1);

            // Answer it
            await ExecuteSpecCommandAsync("answer spec-blocking Q-1 \"Answer\"");

            // Lock now
            var lockResult2 = await ExecuteSpecCommandAsync("lock spec-blocking");
            Assert.Contains("now Locked", lockResult2);

            // Start coordinate task
            var startResult = await ExecuteCoordinateCommandAsync("start T-004 \"Task 4\" --spec spec-blocking");
            Assert.Contains("started with ID", startResult);

            // Pass the default Planning gates + Spec criteria gate
            var store = CoordinatorStore.Instance;
            store.AddEvidence("T-004", "DesignDoc", "Tester", "Summary");
            store.UpdateGate("T-004", "DesignDoc", true);
            store.UpdateGate("T-004", "ResourceCheck", true);

            store.AddEvidence("T-004", "Spec-AC-1", "Tester", "Summary");
            store.UpdateGate("T-004", "Spec-AC-1", true);

            // Transition to Execution - should succeed
            var transResult1 = await ExecuteCoordinateCommandAsync("phase T-004 Execution");
            Assert.Contains("transitioned to Execution", transResult1);

            // Let's verify transition was successful
            Assert.True(AppState.Tasks.TryGetValue("T-004", out var st));
            var task = st as CoordinateTask;
            Assert.NotNull(task);
            Assert.Equal(CoordinatePhase.Execution, task.CurrentPhase);

            // Now, let's modify the spec on disk to have an unanswered question (e.g. add Q-2)
            var specStore = new SeedSpecStore(_testWorkspace);
            var spec = await specStore.LoadAsync("spec-blocking");
            Assert.NotNull(spec);

            spec.OpenQuestions.Add(new ClarifyingQuestion { Id = "Q-2", Question = "Another Blocking?", IsBlocking = true, Answer = null });
            await specStore.SaveAsync(spec);

            // Start a new task T-005 with the same spec
            var startResult2 = await ExecuteCoordinateCommandAsync("start T-005 \"Task 5\" --spec spec-blocking");
            Assert.Contains("started with ID", startResult2);

            // Pass the default Planning gates + Spec criteria gate for T-005
            store.AddEvidence("T-005", "DesignDoc", "Tester", "Summary");
            store.UpdateGate("T-005", "DesignDoc", true);
            store.UpdateGate("T-005", "ResourceCheck", true);

            store.AddEvidence("T-005", "Spec-AC-1", "Tester", "Summary");
            store.UpdateGate("T-005", "Spec-AC-1", true);

            // Try transitioning T-005 to Execution - should block because of unanswered question
            var transResult2 = await ExecuteCoordinateCommandAsync("phase T-005 Execution");
            Assert.Contains("Error", transResult2);
            Assert.Contains("has unanswered blocking questions", transResult2);

            Assert.True(AppState.Tasks.TryGetValue("T-005", out var st5));
            var task5 = st5 as CoordinateTask;
            Assert.NotNull(task5);
            Assert.Equal(CoordinatePhase.Planning, task5.CurrentPhase);

            // Now answer Q-2
            spec.OpenQuestions.First(q => q.Id == "Q-2").Answer = "Solved";
            await specStore.SaveAsync(spec);

            // Try transitioning again - should succeed
            var transResult3 = await ExecuteCoordinateCommandAsync("phase T-005 Execution");
            Assert.Contains("transitioned to Execution", transResult3);
            Assert.Equal(CoordinatePhase.Execution, task5.CurrentPhase);
        }
    }
}
