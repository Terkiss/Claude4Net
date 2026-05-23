using System.Threading.Tasks;
using Claude4Net.Runtime;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K062SkillTrajectoryMiningTests
    {
        [Fact]
        public async Task MineFailurePatterns_ShouldReturnPatterns()
        {
            var miner = new TrajectoryMiner();
            var patterns = await miner.MineFailurePatternsAsync();
            Assert.NotEmpty(patterns);
        }

        [Fact]
        public void GenerateProposal_ShouldReturnDraft()
        {
            var generator = new SkillProposalGenerator();
            var draft = generator.GenerateProposal("test pattern");

            Assert.NotNull(draft.Id);
            Assert.Equal(Claude4Net.SDK.SkillProposalStatus.Proposed, draft.Status);
        }
    }
}
