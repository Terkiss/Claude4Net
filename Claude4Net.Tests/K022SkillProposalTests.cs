using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Collections.Generic;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K022SkillProposalTests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly SkillRegistryService _registry;

        public K022SkillProposalTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_ProposalTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = null; // Ensure null by default for P1 tests
            _registry = new SkillRegistryService(_tempWorkspace);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, true);
            }
            AppState.CurrentCwd = null;
        }

        [Fact]
        public async Task SkillProposalService_InitializesEmpty()
        {
            var service = new SkillProposalService(_registry);
            await service.LoadAsync(_tempWorkspace);
            Assert.Empty(service.ListProposals());
        }

        [Fact]
        public async Task SkillProposalService_CreatesAndListsProposal()
        {
            var service = new SkillProposalService(_registry);
            await service.LoadAsync(_tempWorkspace);

            var proposal = new SkillProposalRecord
            {
                Title = "Fix bug in parser",
                Rationale = "Handles nested tags better",
                ProposedChanges = "DIFF TEXT",
                Type = SkillProposalType.BugFix
            };

            service.CreateProposal(_tempWorkspace, proposal);
            var list = service.ListProposals();

            Assert.Single(list);
            Assert.StartsWith("PROP-", list[0].Id);
            Assert.Equal(SkillProposalStatus.Draft, list[0].Status);
        }

        [Fact]
        public async Task SkillProposalService_UpdatesStatus()
        {
            var service = new SkillProposalService(_registry);
            await service.LoadAsync(_tempWorkspace);
            var p = new SkillProposalRecord { Id = "PROP-123", Title = "Test" };
            service.CreateProposal(_tempWorkspace, p);

            service.UpdateStatus("PROP-123", SkillProposalStatus.Approved);

            var updated = service.GetProposal("PROP-123");
            Assert.Equal(SkillProposalStatus.Approved, updated?.Status);
        }

        [Fact]
        public async Task SkillProposalService_SavesAndLoads()
        {
            var service1 = new SkillProposalService(_registry);
            await service1.LoadAsync(_tempWorkspace);
            service1.CreateProposal(_tempWorkspace, new SkillProposalRecord { Id = "P1", Title = "Save Test" });
            await service1.SaveAsync(_tempWorkspace);

            var service2 = new SkillProposalService(_registry);
            await service2.LoadAsync(_tempWorkspace);
            var loaded = service2.GetProposal("P1");

            Assert.NotNull(loaded);
            Assert.Equal("Save Test", loaded.Title);
        }

        [Fact]
        public void SkillProposalService_RejectsOutsidePath()
        {
            var service = new SkillProposalService(_registry);
            string outsidePath = Path.Combine(Path.GetTempPath(), "evil.md");

            var p = new SkillProposalRecord { Title = "Evil", TargetPath = outsidePath };

            Assert.Throws<UnauthorizedAccessException>(() => service.CreateProposal(_tempWorkspace, p));
        }

        [Fact]
        public async Task SkillProposalService_ApproveDoesNotModifyFiles()
        {
            // Arrange: Create a fake skill file
            string skillFile = Path.Combine(_tempWorkspace, "my-skill.md");
            string originalContent = "Original Content";
            File.WriteAllText(skillFile, originalContent);

            var service = new SkillProposalService(_registry);
            await service.LoadAsync(_tempWorkspace);

            var p = new SkillProposalRecord
            {
                Id = "PROP-001",
                Title = "Change content",
                TargetPath = skillFile,
                ProposedChanges = "New Content"
            };
            service.CreateProposal(_tempWorkspace, p);

            // Act
            service.UpdateStatus("PROP-001", SkillProposalStatus.Approved);
            await service.SaveAsync(_tempWorkspace);

            // Assert
            string currentContent = File.ReadAllText(skillFile);
            Assert.Equal(originalContent, currentContent); // Must NOT change
        }

        [Fact]
        public async Task SkillProposalService_HandlesUnknownSkillId()
        {
            var service = new SkillProposalService(_registry);
            await service.LoadAsync(_tempWorkspace);

            var p = new SkillProposalRecord { SkillId = "non-existent-skill", Title = "New skill idea" };
            service.CreateProposal(_tempWorkspace, p);

            var created = service.GetProposal(p.Id);
            Assert.Equal("true", created?.Metadata["UnresolvedSkillId"]);
        }

        [Fact]
        public async Task SkillProposalService_P1_WorkspaceLateBindingTest()
        {
            // 1. Initial state: Workspace not set
            AppState.CurrentCwd = null;
            var service = new SkillProposalService(_registry);

            // 2. Simulate SystemBaseDir usage (Doctor or command resolved early)
            string systemBase = Path.Combine(_tempWorkspace, "SystemBase");
            Directory.CreateDirectory(systemBase);
            // Service should not be hard-bound to systemBase even if passed to some methods

            // 3. Late workspace setting
            string realWorkspace = Path.Combine(_tempWorkspace, "RealWorkspace");
            Directory.CreateDirectory(realWorkspace);
            AppState.CurrentCwd = realWorkspace;

            // 4. Operation time binding: Load and Create in real workspace
            await service.LoadAsync(realWorkspace);
            service.CreateProposal(realWorkspace, new SkillProposalRecord { Title = "Late binding test" });
            await service.SaveAsync(realWorkspace);

            // 5. Verification: Data should be in RealWorkspace, NOT in SystemBase
            Assert.True(File.Exists(Path.Combine(realWorkspace, ".claude4net", "skill-proposals.json")));
            Assert.False(File.Exists(Path.Combine(systemBase, ".claude4net", "skill-proposals.json")));
        }

        [Fact]
        public async Task SkillProposalService_P1_FailClosedWithoutWorkspace()
        {
            AppState.CurrentCwd = null;
            var service = new SkillProposalService(_registry);

            // Load/Save should throw if root is null/empty
            await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync(""));
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(null!));
        }
    }
}
