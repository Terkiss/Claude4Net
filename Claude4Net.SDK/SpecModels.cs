using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public enum SeedSpecStatus
    {
        Draft,
        NeedsClarification,
        Locked,
        Superseded
    }

    public sealed class SeedSpecRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Goal { get; set; } = string.Empty;
        public string IntentSummary { get; set; } = string.Empty;
        public List<string> InScope { get; set; } = new();
        public List<string> OutOfScope { get; set; } = new();
        public List<AcceptanceCriterion> AcceptanceCriteria { get; set; } = new();
        public List<string> Constraints { get; set; } = new();
        public List<string> Risks { get; set; } = new();
        public List<ClarifyingQuestion> OpenQuestions { get; set; } = new();
        public double AmbiguityScore { get; set; }
        public SeedSpecStatus Status { get; set; } = SeedSpecStatus.Draft;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string SchemaVersion { get; set; } = "1.0";
    }

    public sealed class AcceptanceCriterion
    {
        public string Id { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string EvidenceRequired { get; set; } = string.Empty;
        public bool Required { get; set; } = true;
    }

    public sealed class ClarifyingQuestion
    {
        public string Id { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string? Answer { get; set; }
        public bool IsBlocking { get; set; } = true;
    }
}
