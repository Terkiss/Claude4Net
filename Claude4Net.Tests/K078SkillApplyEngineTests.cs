using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Moq;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K078SkillApplyEngineTests : IDisposable
    {
        private readonly string _workspace;
        private readonly SkillProposalService _proposalService;
        private readonly SkillRegistryService _skillRegistry;

        public K078SkillApplyEngineTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_ApplyEngine_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
            _skillRegistry = new SkillRegistryService(_workspace);
            _proposalService = new SkillProposalService(_skillRegistry);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public async Task Apply_NonApprovedProposal_ShouldThrow()
        {
            await _proposalService.LoadAsync(_workspace);
            var prop = new SkillProposalRecord
            {
                Id = "PROP-101",
                SkillId = "test-skill",
                Status = SkillProposalStatus.Proposed,
                ProposedChanges = "public class NewSkill {}"
            };
            _proposalService.CreateProposal(_workspace, prop);
            await _proposalService.SaveAsync(_workspace);

            var engine = new SkillApplyEngine(_proposalService, _skillRegistry);
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ApplyAsync("PROP-101", _workspace));
        }

        [Fact]
        public async Task Apply_ForbiddenPathMutation_ShouldThrow()
        {
            await _proposalService.LoadAsync(_workspace);

            // Testing SkillId starts with .agents/
            var propAgentsSkill = new SkillProposalRecord
            {
                Id = "PROP-102",
                SkillId = ".agents/core",
                Status = SkillProposalStatus.Approved,
                ProposedChanges = "public class Core {}"
            };
            _proposalService.CreateProposal(_workspace, propAgentsSkill);

            // Testing TargetPath contains .gemini/
            var propGeminiPath = new SkillProposalRecord
            {
                Id = "PROP-103",
                TargetPath = ".gemini/config/some-file.cs",
                Status = SkillProposalStatus.Approved,
                ProposedChanges = "public class Config {}"
            };
            _proposalService.CreateProposal(_workspace, propGeminiPath);

            await _proposalService.SaveAsync(_workspace);

            var engine = new SkillApplyEngine(_proposalService, _skillRegistry);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.ApplyAsync("PROP-102", _workspace));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => engine.ApplyAsync("PROP-103", _workspace));
        }

        [Fact]
        public async Task Apply_PipelineInteractions_VerifiedCorrectly()
        {
            await _proposalService.LoadAsync(_workspace);
            var prop = new SkillProposalRecord
            {
                Id = "PROP-104",
                TargetPath = "skills/my-new-skill.cs",
                Status = SkillProposalStatus.Approved,
                ProposedChanges = "public class MyNewSkill {}"
            };
            _proposalService.CreateProposal(_workspace, prop);
            await _proposalService.SaveAsync(_workspace);

            // Mock Approval Handler
            var mockApproval = new Mock<IRichApprovalHandler>();
            mockApproval.Setup(a => a.RequestApprovalWithDiffAsync(
                It.Is<string>(t => t == "skill-apply"),
                It.Is<string>(id => id == "PROP-104"),
                It.IsNotNull<FileDiffPreview>()
            )).ReturnsAsync(true).Verifiable();

            var engine = new SkillApplyEngine(_proposalService, _skillRegistry, mockApproval.Object);

            // Let's set a custom verifier that returns true
            engine.Verifier = (ws, id) => Task.FromResult(true);

            // Execute Apply
            bool result = await engine.ApplyAsync("PROP-104", _workspace);
            Assert.True(result);

            // Verify approval handler was called
            mockApproval.Verify();

            // Verify file was written
            string appliedFilePath = Path.Combine(_workspace, "skills/my-new-skill.cs");
            Assert.True(File.Exists(appliedFilePath));
            Assert.Equal("public class MyNewSkill {}", await File.ReadAllTextAsync(appliedFilePath));

            // Verify proposal state became Verified (transitions: Approved -> Applied -> Verified)
            await _proposalService.LoadAsync(_workspace);
            var updatedProp = _proposalService.GetProposal("PROP-104");
            Assert.NotNull(updatedProp);
            Assert.Equal(SkillProposalStatus.Verified, updatedProp.Status);

            // Verify checkpoint creation evidence exists
            Assert.Contains(updatedProp.EvidenceReferences, e => e.StartsWith("checkpoint:"));
            Assert.True(updatedProp.Metadata.ContainsKey("CheckpointId"));
            Assert.True(updatedProp.Metadata.ContainsKey("ApplyDiff"));
        }

        [Fact]
        public async Task Apply_VerificationFails_RevertsChangesAndTransitionsToFailed()
        {
            await _proposalService.LoadAsync(_workspace);

            // Let's first create an original file with original content to test reversion
            string targetRelative = "skills/revert-skill.cs";
            string targetAbsolute = Path.Combine(_workspace, targetRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetAbsolute)!);
            await File.WriteAllTextAsync(targetAbsolute, "original content");

            var prop = new SkillProposalRecord
            {
                Id = "PROP-105",
                TargetPath = targetRelative,
                Status = SkillProposalStatus.Approved,
                ProposedChanges = "broken content"
            };
            _proposalService.CreateProposal(_workspace, prop);
            await _proposalService.SaveAsync(_workspace);

            var engine = new SkillApplyEngine(_proposalService, _skillRegistry);

            // Let's set a custom verifier that returns false to simulate compilation failure
            engine.Verifier = (ws, id) => Task.FromResult(false);

            // Execute Apply
            bool result = await engine.ApplyAsync("PROP-105", _workspace);
            Assert.False(result);

            // Verify that the file content was reverted back to "original content" using the checkpoint
            Assert.Equal("original content", await File.ReadAllTextAsync(targetAbsolute));

            // Verify proposal state transitioned to Failed
            await _proposalService.LoadAsync(_workspace);
            var updatedProp = _proposalService.GetProposal("PROP-105");
            Assert.Equal(SkillProposalStatus.Failed, updatedProp?.Status);
        }

        [Fact]
        public async Task Apply_NewSkill_DefaultsToLocalWorkspaceSkillsDirectory()
        {
            await _proposalService.LoadAsync(_workspace);
            var prop = new SkillProposalRecord
            {
                Id = "PROP-106",
                SkillId = "new-local-skill",
                Status = SkillProposalStatus.Approved,
                ProposedChanges = "public class NewLocalSkill {}"
            };
            _proposalService.CreateProposal(_workspace, prop);
            await _proposalService.SaveAsync(_workspace);

            var engine = new SkillApplyEngine(_proposalService, _skillRegistry);
            engine.Verifier = (ws, id) => Task.FromResult(true);

            bool result = await engine.ApplyAsync("PROP-106", _workspace);
            Assert.True(result);

            // Verify it was saved to .claude4net/skills/new-local-skill.cs
            string expectedPath = Path.Combine(_workspace, ".claude4net", "skills", "new-local-skill.cs");
            Assert.True(File.Exists(expectedPath));
            Assert.Equal("public class NewLocalSkill {}", await File.ReadAllTextAsync(expectedPath));
        }

        [Fact]
        public async Task Apply_GlobalSkill_AllowsMutationUnderSystemBaseDir()
        {
            string originalSystemBaseDir = AppState.SystemBaseDir;
            string tempSystemBase = Path.Combine(Path.GetTempPath(), "Claude4Net_SystemBase_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempSystemBase);
            AppState.SystemBaseDir = tempSystemBase;

            try
            {
                await _proposalService.LoadAsync(_workspace);
                var prop = new SkillProposalRecord
                {
                    Id = "PROP-107",
                    SkillId = "new-global-skill",
                    Status = SkillProposalStatus.Approved,
                    ProposedChanges = "public class NewGlobalSkill {}"
                };
                prop.Metadata["IsGlobal"] = "true";
                _proposalService.CreateProposal(_workspace, prop);
                await _proposalService.SaveAsync(_workspace);

                var engine = new SkillApplyEngine(_proposalService, _skillRegistry);
                engine.Verifier = (ws, id) => Task.FromResult(true);

                bool result = await engine.ApplyAsync("PROP-107", _workspace);
                Assert.True(result);

                // Verify it was saved under AppState.SystemBaseDir/skills/new-global-skill.cs
                string expectedPath = Path.Combine(tempSystemBase, "skills", "new-global-skill.cs");
                Assert.True(File.Exists(expectedPath));
                Assert.Equal("public class NewGlobalSkill {}", await File.ReadAllTextAsync(expectedPath));
            }
            finally
            {
                AppState.SystemBaseDir = originalSystemBaseDir;
                try { Directory.Delete(tempSystemBase, true); } catch { }
            }
        }
    }
}
