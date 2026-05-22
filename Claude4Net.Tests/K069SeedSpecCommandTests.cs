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
    public class K069SeedSpecCommandTests : IDisposable
    {
        private readonly string _testWorkspace;
        private readonly IServiceProvider _serviceProvider;

        public K069SeedSpecCommandTests()
        {
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_SpecCmd_" + Guid.NewGuid().ToString("N"));
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

        private async Task<string> ExecuteSpecCommandAsync(string arguments)
        {
            var cmd = CommandRegistry.FindCommand("spec");
            Assert.NotNull(cmd);
            var result = await cmd.Handler!(arguments, _serviceProvider);
            return (string)result;
        }

        [Fact]
        public async Task SpecCommand_NoArgs_ReturnsUsage()
        {
            var result = await ExecuteSpecCommandAsync("");
            Assert.Contains("Usage:", result);
            Assert.Contains("/spec list", result);
        }

        [Fact]
        public async Task SpecCommand_List_NoSpecs_ReturnsNoSpecsMessage()
        {
            var result = await ExecuteSpecCommandAsync("list");
            Assert.Contains("No specs found in workspace", result);
        }

        [Fact]
        public async Task SpecCommand_NewAndShow_PathTraversal_Rejected()
        {
            // Traverse
            var resultNew = await ExecuteSpecCommandAsync("new ../badspec \"Traversal Title\"");
            Assert.Contains("Error", resultNew);
            Assert.Contains("Invalid Spec ID", resultNew);

            var resultShow = await ExecuteSpecCommandAsync("show ../badspec");
            Assert.Contains("Error", resultShow);
            Assert.Contains("Invalid Spec ID", resultShow);
        }

        [Fact]
        public async Task SpecCommand_NewAndShow_Valid_Succeeds()
        {
            // Create spec
            var resultNew = await ExecuteSpecCommandAsync("new spec-100 \"My First Spec\"");
            Assert.Contains("created successfully", resultNew);
            Assert.Contains("spec-100", resultNew);

            // Create existing spec - should fail
            var resultDup = await ExecuteSpecCommandAsync("new spec-100 \"Duplicate\"");
            Assert.Contains("already exists", resultDup);

            // Show spec
            var resultShow = await ExecuteSpecCommandAsync("show spec-100");
            Assert.Contains("My First Spec", resultShow);
            Assert.Contains("Draft", resultShow);
        }

        [Fact]
        public async Task SpecCommand_QuestionAndAnswer_Workflow_Succeeds()
        {
            // Setup spec
            await ExecuteSpecCommandAsync("new spec-200 \"Question Spec\"");

            // Add question
            var resultQ1 = await ExecuteSpecCommandAsync("question spec-200 \"Is this a test?\"");
            Assert.Contains("Blocking question 'Q-1' added", resultQ1);

            // Add second question to verify auto-increment ID
            var resultQ2 = await ExecuteSpecCommandAsync("question spec-200 \"Another question?\"");
            Assert.Contains("Blocking question 'Q-2' added", resultQ2);

            // Show to verify questions exist and are unanswered
            var showResult = await ExecuteSpecCommandAsync("show spec-200");
            Assert.Contains("Is this a test?", showResult);
            Assert.Contains("Unanswered", showResult);

            // Answer question
            var resultA1 = await ExecuteSpecCommandAsync("answer spec-200 Q-1 \"Yes, it is.\"");
            Assert.Contains("answered successfully", resultA1);

            // Show to verify question is answered
            var showResult2 = await ExecuteSpecCommandAsync("show spec-200");
            Assert.Contains("Answered:[/] Yes, it is.", showResult2);
        }

        [Fact]
        public async Task SpecCommand_CriteriaAdd_Succeeds()
        {
            // Setup spec
            await ExecuteSpecCommandAsync("new spec-300 \"Criteria Spec\"");

            // Add criteria
            var resultC1 = await ExecuteSpecCommandAsync("criteria add spec-300 \"Must compile cleanly\"");
            Assert.Contains("Required acceptance criterion 'AC-1' added", resultC1);

            var resultC2 = await ExecuteSpecCommandAsync("criteria add spec-300 \"Must pass unit tests\"");
            Assert.Contains("Required acceptance criterion 'AC-2' added", resultC2);

            // Show to verify criteria
            var showResult = await ExecuteSpecCommandAsync("show spec-300");
            Assert.Contains("Must compile cleanly", showResult);
            Assert.Contains("(Required)", showResult);
        }

        [Fact]
        public async Task SpecCommand_Lock_ValidationLogic_Works()
        {
            // Setup spec
            await ExecuteSpecCommandAsync("new spec-400 \"Lock Spec\"");

            // Try lock with empty criteria and no questions (should fail because no criteria exists)
            var resultLock1 = await ExecuteSpecCommandAsync("lock spec-400");
            Assert.Contains("At least one required acceptance criterion is needed", resultLock1);

            // Add criterion
            await ExecuteSpecCommandAsync("criteria add spec-400 \"Pass tests\"");

            // Lock now should succeed (no unanswered blocking questions, has criteria)
            var resultLock2 = await ExecuteSpecCommandAsync("lock spec-400");
            Assert.Contains("is now Locked", resultLock2);

            // Unlock not supported, but let's test unanswered blocking questions blocks lock
            // We create a new spec spec-401 for this
            await ExecuteSpecCommandAsync("new spec-401 \"Lock Fail Spec\"");
            await ExecuteSpecCommandAsync("criteria add spec-401 \"Pass tests\"");
            await ExecuteSpecCommandAsync("question spec-401 \"A blocking question?\"");

            // Try lock (should fail due to unanswered question Q-1)
            var resultLock3 = await ExecuteSpecCommandAsync("lock spec-401");
            Assert.Contains("Unanswered blocking questions exist: Q-1", resultLock3);

            // Answer it
            await ExecuteSpecCommandAsync("answer spec-401 Q-1 \"Answered\"");

            // Lock now succeeds
            var resultLock4 = await ExecuteSpecCommandAsync("lock spec-401");
            Assert.Contains("is now Locked", resultLock4);
        }

        [Fact]
        public async Task SpecCommand_Attach_SyncsGatesAndValidatesStatus()
        {
            // Setup task
            var task = CoordinatorStore.Instance.CreateTask("T-100", "Task 100", "Testing attach");

            // Setup spec
            await ExecuteSpecCommandAsync("new spec-500 \"Attach Spec\"");
            await ExecuteSpecCommandAsync("criteria add spec-500 \"AC Desc\"");

            // Try attaching when spec is Draft (should fail)
            var resultAttachDraft = await ExecuteSpecCommandAsync("attach spec-500 T-100");
            Assert.Contains("Error", resultAttachDraft);
            Assert.Contains("is not in Locked status", resultAttachDraft);

            // Lock spec
            await ExecuteSpecCommandAsync("lock spec-500");

            // Attach now
            var resultAttachLocked = await ExecuteSpecCommandAsync("attach spec-500 T-100");
            Assert.Contains("successfully attached", resultAttachLocked);
            Assert.Contains("Spec-AC-1", resultAttachLocked);

            // Check task spec details
            Assert.Equal("spec-500", task.SpecId);
            Assert.NotNull(task.SpecLockedAt);
            Assert.Contains(task.Gates, g => g.Name == "Spec-AC-1");
        }
    }
}
