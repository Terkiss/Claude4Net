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

    public class CoordinateGate
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPassed { get; set; }
        public string? Comments { get; set; }
        public DateTime? UpdatedAt { get; set; }
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

        public CoordinateTask()
        {
            Type = "Coordinate";
            Status = "Planning";
        }
    }
}
