using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// Tracks skill quality and usage statistics.
    /// </summary>
    public class SkillQualityMetrics
    {
        /// <summary>Successful usage count.</summary>
        public int SuccessCount { get; set; }

        /// <summary>Failed usage count.</summary>
        public int FailureCount { get; set; }

        /// <summary>Average satisfaction or quality score, from 0.0 to 1.0.</summary>
        public double AverageScore { get; set; }

        /// <summary>Last usage timestamp.</summary>
        public DateTime? LastUsed { get; set; }
    }

    /// <summary>
    /// Registry record for a single skill.
    /// </summary>
    public class SkillRegistryRecord
    {
        /// <summary>Stable skill ID.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Human-readable display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Skill source file path, either workspace-relative or absolute.</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>Alternative names used to resolve this skill.</summary>
        public List<string> Aliases { get; set; } = new();

        /// <summary>Skill version.</summary>
        public string Version { get; set; } = "1.0";

        /// <summary>Skill description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Quality metrics.</summary>
        public SkillQualityMetrics Metrics { get; set; } = new();

        /// <summary>Additional metadata.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Root model persisted to skill-registry.json.
    /// </summary>
    public class SkillRegistryRoot
    {
        /// <summary>Registered skill records.</summary>
        public List<SkillRegistryRecord> Skills { get; set; } = new();

        /// <summary>Registry schema version.</summary>
        public string SchemaVersion { get; set; } = "1.0";

        /// <summary>Last registry update timestamp.</summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Status of a skill evolution proposal.
    /// </summary>
    public enum SkillProposalStatus
    {
        Draft,
        Proposed,
        Approved,
        Rejected,
        Superseded, Applied
    }

    /// <summary>
    /// Type of improvement being proposed.
    /// </summary>
    public enum SkillProposalType
    {
        BugFix,
        Feature,
        Refactoring,
        Optimization,
        Documentation
    }

    /// <summary>
    /// Represents a proposed improvement for a skill.
    /// </summary>
    public class SkillProposalRecord
    {
        /// <summary>Unique proposal ID (e.g., PROP-001).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Short summary title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Detailed description.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Target skill ID. Can be null if targeting a new or unresolved skill.</summary>
        public string? SkillId { get; set; }

        /// <summary>Target source path if skill ID is not yet assigned or for external skills.</summary>
        public string? TargetPath { get; set; }

        /// <summary>Type of the proposal.</summary>
        public SkillProposalType Type { get; set; } = SkillProposalType.BugFix;

        /// <summary>Detailed rationale for why this change is needed.</summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>The suggested change text or patch preview.</summary>
        public string ProposedChanges { get; set; } = string.Empty;

        /// <summary>Current status of the proposal.</summary>
        public SkillProposalStatus Status { get; set; } = SkillProposalStatus.Draft;

        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Last update timestamp.</summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Optional list of evidence IDs or links related to this proposal.</summary>
        public List<string> EvidenceReferences { get; set; } = new();

        /// <summary>Additional metadata.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Result of a dry-run skill validation.
    /// </summary>
    public class SkillValidationResult
    {
        public string ProposalId { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public int EstimatedPassRate { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Root model for skill proposals persistence.
    /// </summary>
    public class SkillProposalRoot
    {
        public List<SkillProposalRecord> Proposals { get; set; } = new();
        public string SchemaVersion { get; set; } = "1.0";
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
