using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K061SkillProposalApplyTests : IDisposable
    {
        private readonly string _workspace;
        private readonly SkillProposalService _service;
        private readonly SkillProposalApplier _applier;
        private readonly SkillRegistryService _registry;

        public K061SkillProposalApplyTests()
        {
            _workspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Apply_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_workspace);
            _registry = new SkillRegistryService(_workspace);
            _service = new SkillProposalService(_registry);
            _applier = new SkillProposalApplier(_service);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspace, true); } catch { }
        }

        [Fact]
        public async Task Apply_UnapprovedProposal_ShouldThrow()
        {
            await _service.LoadAsync(_workspace);
            var prop = new SkillProposalRecord { Id = "PROP-1", Status = SkillProposalStatus.Proposed };
            _service.CreateProposal(_workspace, prop);
            await _service.SaveAsync(_workspace);

            await Assert.ThrowsAsync<InvalidOperationException>(() => _applier.ApplyAsync("PROP-1", _workspace));
        }

        [Fact]
        public async Task Apply_AgentsPath_ShouldThrow()
        {
            await _service.LoadAsync(_workspace);
            var prop = new SkillProposalRecord { Id = "PROP-2", SkillId = ".agents/core", Status = SkillProposalStatus.Approved };
            _service.CreateProposal(_workspace, prop);
            await _service.SaveAsync(_workspace);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _applier.ApplyAsync("PROP-2", _workspace));
        }

        [Fact]
        public async Task Apply_ValidApprovedProposal_ShouldMarkApplied()
        {
            await _service.LoadAsync(_workspace);
            var prop = new SkillProposalRecord { Id = "PROP-3", SkillId = "normal-skill", Status = SkillProposalStatus.Approved };
            _service.CreateProposal(_workspace, prop);
            await _service.SaveAsync(_workspace);

            var result = await _applier.ApplyAsync("PROP-3", _workspace);
            Assert.True(result);

            await _service.LoadAsync(_workspace);
            var loaded = _service.GetProposal("PROP-3");
            Assert.True(loaded?.Status == SkillProposalStatus.Applied || loaded?.Status == SkillProposalStatus.Verified);
        }
    }
}
