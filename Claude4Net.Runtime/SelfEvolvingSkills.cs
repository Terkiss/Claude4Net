using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class SkillUsageRecorder
    {
        public void Record(string skillId, bool success, int score)
        {
            // Placeholder for MVP
        }
    }

    public class TrajectoryMiner
    {
        public List<string> MineFailurePatterns()
        {
            return new List<string> { "Frequent FileSystemException on Windows paths" };
        }
    }

    public class SkillProposalGenerator
    {
        public SkillProposalRecord GenerateProposal(string failurePattern)
        {
            return new SkillProposalRecord
            {
                Id = "PROP-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                SkillId = "unknown",
                Title = $"Fix for {failurePattern}",
                Status = SkillProposalStatus.Proposed
            };
        }
    }

    public class SkillProposalApplier
    {
        private readonly SkillProposalService _proposalService;

        public SkillProposalApplier(SkillProposalService proposalService)
        {
            _proposalService = proposalService;
        }

        public async Task<bool> ApplyAsync(string proposalId, string workspaceRoot)
        {
            await _proposalService.LoadAsync(workspaceRoot);
            var proposal = _proposalService.GetProposal(proposalId);
            if (proposal == null) throw new InvalidOperationException("Proposal not found.");

            if (proposal.Status != SkillProposalStatus.Approved)
            {
                throw new InvalidOperationException("Only approved proposals can be applied.");
            }

            if (proposal.SkillId != null && proposal.SkillId.StartsWith(".agents/"))
            {
                throw new UnauthorizedAccessException("Cannot mutate .agents/ directly.");
            }

            _proposalService.UpdateStatus(proposalId, SkillProposalStatus.Applied);
            await _proposalService.SaveAsync(workspaceRoot);
            return true;
        }
    }
}
