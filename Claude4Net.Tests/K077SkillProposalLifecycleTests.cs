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
    public class K077SkillProposalLifecycleTests : IDisposable
    {
        private readonly string _testWorkspace;
        private readonly IServiceProvider _serviceProvider;
        private readonly SkillRegistryService _registry;
        private readonly SkillProposalService _proposalService;

        public K077SkillProposalLifecycleTests()
        {
            _testWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_K077_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspace);
            AppState.CurrentCwd = _testWorkspace;
            AppState.Tasks.Clear();

            _registry = new SkillRegistryService(_testWorkspace);
            _proposalService = new SkillProposalService(_registry);

            var services = new ServiceCollection();
            services.AddSingleton(_registry);
            services.AddSingleton(_proposalService);
            _serviceProvider = services.BuildServiceProvider();
        }

        public void Dispose()
        {
            AppState.Tasks.Clear();
            AppState.CurrentCwd = null;
            try
            {
                if (Directory.Exists(_testWorkspace))
                {
                    Directory.Delete(_testWorkspace, true);
                }
            }
            catch { }
        }

        private async Task<string> ExecuteSkillCommandAsync(string arguments)
        {
            var cmd = CommandRegistry.FindCommand("skill");
            Assert.NotNull(cmd);
            var result = await cmd.Handler!(arguments, _serviceProvider);
            return result;
        }

        [Fact]
        public void StateMachine_Transitions_StrictlyEnforced()
        {
            var p = new SkillProposalRecord { Id = "PROP-T1", Title = "Transition Test" };
            _proposalService.CreateProposal(_testWorkspace, p);

            // Initial state is Draft
            Assert.Equal(SkillProposalStatus.Draft, p.Status);

            // Valid transitions from Draft: Proposed, Approved, Rejected
            // Draft -> Proposed
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Proposed);
            Assert.Equal(SkillProposalStatus.Proposed, p.Status);

            // Proposed -> Approved (Reset status to Draft first to check direct path)
            p.Status = SkillProposalStatus.Draft;
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Approved);
            Assert.Equal(SkillProposalStatus.Approved, p.Status);

            // Approved -> Applied
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Applied);
            Assert.Equal(SkillProposalStatus.Applied, p.Status);

            // Applied -> Verified
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Verified);
            Assert.Equal(SkillProposalStatus.Verified, p.Status);

            // Test Applied -> Failed
            p.Status = SkillProposalStatus.Applied;
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Failed);
            Assert.Equal(SkillProposalStatus.Failed, p.Status);

            // Test Failed -> Superseded
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded);
            Assert.Equal(SkillProposalStatus.Superseded, p.Status);

            // Test Approved -> Superseded
            p.Status = SkillProposalStatus.Approved;
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded);
            Assert.Equal(SkillProposalStatus.Superseded, p.Status);

            // Test Draft -> Rejected
            p.Status = SkillProposalStatus.Draft;
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Rejected);
            Assert.Equal(SkillProposalStatus.Rejected, p.Status);

            // Test Proposed -> Rejected
            p.Status = SkillProposalStatus.Proposed;
            _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Rejected);
            Assert.Equal(SkillProposalStatus.Rejected, p.Status);

            // Invalid transitions should throw InvalidOperationException
            p.Status = SkillProposalStatus.Draft;
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Applied));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Verified));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Failed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded));

            p.Status = SkillProposalStatus.Proposed;
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Applied));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Verified));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Failed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded));

            p.Status = SkillProposalStatus.Approved;
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Proposed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Rejected));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Verified));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Failed));

            p.Status = SkillProposalStatus.Applied;
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Proposed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Approved));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Rejected));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded));

            p.Status = SkillProposalStatus.Verified;
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Proposed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Approved));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Rejected));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Applied));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Failed));
            Assert.Throws<InvalidOperationException>(() => _proposalService.UpdateStatus("PROP-T1", SkillProposalStatus.Superseded));
        }

        [Fact]
        public void ProposalValidation_DryRunChecks_CorrectlyEvaluated()
        {
            // Missing fields validation
            var pEmpty = new SkillProposalRecord { Id = "PROP-V1" };
            var resultEmpty = _proposalService.ValidateProposal(pEmpty);
            Assert.False(resultEmpty.IsValid);
            Assert.Contains("Missing Title", resultEmpty.Errors);
            Assert.Contains("Missing Description", resultEmpty.Errors);
            Assert.Contains("Missing TargetPath", resultEmpty.Errors);

            // Valid metadata but low score
            var pLowScore = new SkillProposalRecord
            {
                Id = "PROP-V2",
                Title = "Validation Test",
                Description = "A description",
                TargetPath = "my-skill.md",
                Rationale = "Too short", // Length <= 20
                ProposedChanges = "Too short changes" // Length <= 50
            };
            var resultLowScore = _proposalService.ValidateProposal(pLowScore);
            Assert.False(resultLowScore.IsValid);
            Assert.Empty(resultLowScore.Errors);
            Assert.True(resultLowScore.EstimatedPassRate < 60);

            // Valid and high score (Rationale > 20 (+40), ProposedChanges > 50 (+40) = 80 >= 60)
            var pValid = new SkillProposalRecord
            {
                Id = "PROP-V3",
                Title = "Validation Test",
                Description = "A description",
                TargetPath = "my-skill.md",
                Rationale = "This is a detailed rationale that has more than 20 characters.",
                ProposedChanges = "This is a detailed set of proposed changes that has more than 50 characters to achieve high pass score."
            };
            var resultValid = _proposalService.ValidateProposal(pValid);
            Assert.True(resultValid.IsValid);
            Assert.Empty(resultValid.Errors);
            Assert.Equal(80, resultValid.EstimatedPassRate);
        }

        [Fact]
        public async Task CommandHandlers_ExecuteAllSubCommands_RenderExpectedOutcome()
        {
            // 1. /skill analyze
            var analyzeResult = await ExecuteSkillCommandAsync("analyze");
            Assert.Contains("Skill Registry Diagnostic", analyzeResult);

            // 2. /skill proposals (no proposals initially)
            var proposalsResult = await ExecuteSkillCommandAsync("proposals");
            Assert.Contains("No skill proposals found", proposalsResult);

            // 3. /skill propose
            var proposeResult = await ExecuteSkillCommandAsync("propose skill-100 \"Detailed summary of the new proposal\"");
            Assert.Contains("created successfully", proposeResult);

            // Reload to find the proposal ID
            await _proposalService.LoadAsync(_testWorkspace);
            var prop = _proposalService.ListProposals().First();
            string propId = prop.Id;

            // 4. /skill validate
            var validateResult = await ExecuteSkillCommandAsync($"validate {propId}");
            Assert.Contains("Proposal Validation Details", validateResult);
            Assert.Contains("INVALID", validateResult); // Since it has low score / missing description

            // 5. /skill approve on invalid proposal should fail
            var approveFailResult = await ExecuteSkillCommandAsync($"approve {propId}");
            Assert.Contains("Error approving proposal", approveFailResult);

            // Make the proposal valid by setting required properties
            await _proposalService.LoadAsync(_testWorkspace);
            prop = _proposalService.GetProposal(propId);
            Assert.NotNull(prop);
            prop.Description = "Detailed description";
            prop.TargetPath = "my-skill.md";
            prop.Rationale = "This is a detailed rationale that has more than 20 characters.";
            prop.ProposedChanges = "This is a detailed set of proposed changes that has more than 50 characters to achieve high pass score.";
            await _proposalService.SaveAsync(_testWorkspace);

            // 6. /skill approve on valid proposal should succeed
            var approveSuccessResult = await ExecuteSkillCommandAsync($"approve {propId}");
            Assert.Contains("Approved successfully", approveSuccessResult);

            // Check status is Approved
            await _proposalService.LoadAsync(_testWorkspace);
            Assert.Equal(SkillProposalStatus.Approved, _proposalService.GetProposal(propId)?.Status);

            // 7. /skill apply should succeed from Approved status
            var applyResult = await ExecuteSkillCommandAsync($"apply {propId}");
            Assert.Contains("Applied successfully", applyResult);

            // Check status is Applied or Verified
            await _proposalService.LoadAsync(_testWorkspace);
            var status = _proposalService.GetProposal(propId)?.Status;
            Assert.True(status == SkillProposalStatus.Applied || status == SkillProposalStatus.Verified);

            // Test Reject command
            // Create another proposal
            var proposeResult2 = await ExecuteSkillCommandAsync("propose skill-200 \"Reject me\"");
            await _proposalService.LoadAsync(_testWorkspace);
            var prop2 = _proposalService.ListProposals().First(x => x.Title.Contains("Reject me"));
            string prop2Id = prop2.Id;

            // 8. /skill reject should succeed
            var rejectResult = await ExecuteSkillCommandAsync($"reject {prop2Id}");
            Assert.Contains("Rejected successfully", rejectResult);

            // Check status is Rejected
            await _proposalService.LoadAsync(_testWorkspace);
            Assert.Equal(SkillProposalStatus.Rejected, _proposalService.GetProposal(prop2Id)?.Status);
        }
    }
}
