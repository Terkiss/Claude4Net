using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// Manages skill evolution proposals.
    /// Provides a safe, metadata-only workflow for proposing improvements without mutating skill sources.
    /// Operates on a per-workspace basis provided at operation time.
    /// </summary>
    public class SkillProposalService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly SkillRegistryService _skillRegistry;
        private SkillProposalRoot _root = new();

        public SkillRegistryService SkillRegistry => _skillRegistry;

        public SkillProposalService(SkillRegistryService skillRegistry)
        {
            _skillRegistry = skillRegistry ?? throw new ArgumentNullException(nameof(skillRegistry));
        }

        private string GetProposalPath(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new ArgumentException("Workspace root is required for skill proposal operations.");

            string baseDir = Path.Combine(workspaceRoot, ".claude4net");
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);

            return Path.Combine(baseDir, "skill-proposals.json");
        }

        public async Task LoadAsync(string workspaceRoot)
        {
            string path = GetProposalPath(workspaceRoot);
            if (File.Exists(path))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(path);
                    _root = JsonSerializer.Deserialize<SkillProposalRoot>(json) ?? new SkillProposalRoot();
                }
                catch
                {
                    _root = new SkillProposalRoot();
                }
            }
            else
            {
                _root = new SkillProposalRoot();
            }
        }

        public async Task SaveAsync(string workspaceRoot)
        {
            string path = GetProposalPath(workspaceRoot);
            _root.LastUpdatedAt = DateTime.UtcNow;
            string json = JsonSerializer.Serialize(_root, _jsonOptions);
            await File.WriteAllTextAsync(path, json);
        }

        /// <summary>
        /// Creates a new proposal within a specific workspace.
        /// </summary>
        public void CreateProposal(string workspaceRoot, SkillProposalRecord proposal)
        {
            if (string.IsNullOrWhiteSpace(proposal.Id))
            {
                int nextNum = _root.Proposals.Count + 1;
                proposal.Id = $"PROP-{nextNum:D3}";
            }

            if (_root.Proposals.Any(p => p.Id == proposal.Id))
                throw new ArgumentException($"Proposal ID '{proposal.Id}' already exists.");

            // Validate TargetPath if provided
            if (!string.IsNullOrEmpty(proposal.TargetPath))
            {
                ValidatePath(workspaceRoot, proposal.TargetPath);
            }

            // Validate SkillId if provided
            if (!string.IsNullOrEmpty(proposal.SkillId))
            {
                var skill = _skillRegistry.ResolveSkill(proposal.SkillId);
                if (skill == null)
                {
                    // If skill ID is not found, we mark it but don't block (might be a new skill)
                    proposal.Metadata["UnresolvedSkillId"] = "true";
                }
            }

            proposal.CreatedAt = DateTime.UtcNow;
            proposal.UpdatedAt = DateTime.UtcNow;
            _root.Proposals.Add(proposal);
        }

        /// <summary>
        /// Updates a proposal's status. Does NOT apply changes to skill files.
        /// </summary>
        public void UpdateStatus(string proposalId, SkillProposalStatus status)
        {
            var proposal = _root.Proposals.FirstOrDefault(p => p.Id == proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            var current = proposal.Status;

            // Validate transition
            bool isValid = status switch
            {
                SkillProposalStatus.Proposed => current == SkillProposalStatus.Draft,
                SkillProposalStatus.Approved => current == SkillProposalStatus.Draft || current == SkillProposalStatus.Proposed,
                SkillProposalStatus.Rejected => current == SkillProposalStatus.Draft || current == SkillProposalStatus.Proposed,
                SkillProposalStatus.Applied => current == SkillProposalStatus.Approved,
                SkillProposalStatus.Verified => current == SkillProposalStatus.Applied,
                SkillProposalStatus.Failed => current == SkillProposalStatus.Applied,
                SkillProposalStatus.Superseded => current == SkillProposalStatus.Approved || current == SkillProposalStatus.Failed,
                _ => false
            };

            if (!isValid)
            {
                throw new InvalidOperationException($"Invalid status transition from {current} to {status}.");
            }

            proposal.Status = status;
            proposal.UpdatedAt = DateTime.UtcNow;
        }

        public async Task ApproveProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Draft && proposal.Status != SkillProposalStatus.Proposed)
            {
                throw new InvalidOperationException($"Proposal must be in Draft or Proposed state to be approved. Current: {proposal.Status}");
            }

            var validation = ValidateProposal(proposal);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException($"Proposal validation failed: {string.Join(", ", validation.Errors)}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Approved);
            await SaveAsync(workspaceRoot);
        }

        public async Task RejectProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Draft && proposal.Status != SkillProposalStatus.Proposed)
            {
                throw new InvalidOperationException($"Proposal must be in Draft or Proposed state to be rejected. Current: {proposal.Status}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Rejected);
            await SaveAsync(workspaceRoot);
        }

        public async Task ApplyProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Approved)
            {
                throw new InvalidOperationException($"Proposal must be in Approved state to be applied. Current: {proposal.Status}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Applied);
            await SaveAsync(workspaceRoot);
        }

        public async Task VerifyProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Applied)
            {
                throw new InvalidOperationException($"Proposal must be in Applied state to be verified. Current: {proposal.Status}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Verified);
            await SaveAsync(workspaceRoot);
        }

        public async Task FailProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Applied)
            {
                throw new InvalidOperationException($"Proposal must be in Applied state to fail. Current: {proposal.Status}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Failed);
            await SaveAsync(workspaceRoot);
        }

        public async Task SupersedeProposalAsync(string workspaceRoot, string proposalId)
        {
            await LoadAsync(workspaceRoot);
            var proposal = GetProposal(proposalId);
            if (proposal == null) throw new KeyNotFoundException($"Proposal '{proposalId}' not found.");

            if (proposal.Status != SkillProposalStatus.Approved && proposal.Status != SkillProposalStatus.Failed)
            {
                throw new InvalidOperationException($"Proposal must be in Approved or Failed state to be superseded. Current: {proposal.Status}");
            }

            UpdateStatus(proposalId, SkillProposalStatus.Superseded);
            await SaveAsync(workspaceRoot);
        }

        public SkillProposalRecord? GetProposal(string proposalId)
        {
            return _root.Proposals.FirstOrDefault(p => p.Id == proposalId);
        }

        public IReadOnlyList<SkillProposalRecord> ListProposals() => _root.Proposals.AsReadOnly();

        /// <summary>
        /// Performs a dry-run validation of a proposal.
        /// Checks for syntax, required metadata, and estimated impact without applying changes.
        /// </summary>
        public SkillValidationResult ValidateProposal(SkillProposalRecord proposal)
        {
            var result = new SkillValidationResult { ProposalId = proposal.Id };

            // 1. Basic Metadata Check
            if (string.IsNullOrEmpty(proposal.Title)) result.Errors.Add("Missing Title");
            if (string.IsNullOrEmpty(proposal.Description)) result.Errors.Add("Missing Description");

            // 2. Resource Path Check
            if (string.IsNullOrEmpty(proposal.TargetPath)) result.Errors.Add("Missing TargetPath");

            // 3. Simulated "Pass Rate" based on metadata quality (K026)
            int score = 0;
            if (proposal.Rationale.Length > 20) score += 40;
            if (proposal.ProposedChanges.Length > 50) score += 40;
            if (proposal.Metadata.Count > 0) score += 20;

            result.EstimatedPassRate = score;
            result.IsValid = result.Errors.Count == 0 && score >= 60;

            return result;
        }

        private void ValidatePath(string workspaceRoot, string path)
        {
            string workspaceRootFull = Path.GetFullPath(workspaceRoot);
            string workspaceRootWithSeparator = workspaceRootFull.EndsWith(Path.DirectorySeparatorChar)
                ? workspaceRootFull
                : workspaceRootFull + Path.DirectorySeparatorChar;

            // Reuse logic from SkillRegistryService for consistency
            string fullPath = _skillRegistry.ResolveFinalPath(Path.IsPathRooted(path) ? path : Path.Combine(workspaceRootFull, path));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            bool isAtRoot = fullPath.Equals(workspaceRootFull, comparison);
            bool isInside = fullPath.StartsWith(workspaceRootWithSeparator, comparison);

            if (!isAtRoot && !isInside)
                throw new UnauthorizedAccessException($"Path '{path}' is outside safe boundaries of current workspace.");

            // Proposals can TARGET .agents/ for improvements, but the service must never mutate them.
        }
    }
}
