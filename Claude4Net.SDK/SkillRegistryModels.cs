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
}
