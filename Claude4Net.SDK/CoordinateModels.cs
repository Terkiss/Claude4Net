using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public enum CoordinatePhase
    {
        Planning,
        Execution,
        Verification,
        Completed,
        Failed
    }

    public enum ReviewerDecision
    {
        Pending,
        Approved,
        Rejected,
        RequestChanges
    }

    public class CoordinateEvidence
    {
        public string Id { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
        public string Author { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public CoordinatePhase Phase { get; set; }
        public string GateName { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string? Details { get; set; }
    }

    public class CoordinateGate
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPassed { get; set; }
        public bool IsEvidenceRequired { get; set; } = true;
        public string? Comments { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public List<CoordinateEvidence> Evidences { get; set; } = new();
        public string? ApprovedBy { get; set; }
    }

    public class CoordinateTask : TaskStateBase
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public CoordinatePhase CurrentPhase { get; set; } = CoordinatePhase.Planning;
        public List<CoordinateGate> Gates { get; set; } = new();
        public ReviewerDecision ReviewStatus { get; set; } = ReviewerDecision.Pending;
        public string? AssignedAgent { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        public List<string> History { get; set; } = new();
        
        // Merge Readiness
        public double ReadinessScore { get; set; }
        public List<string> Blockers { get; set; } = new();

        public CoordinateTask()
        {
            Type = "Coordinate";
            Status = "Planning";
        }
    }
}
