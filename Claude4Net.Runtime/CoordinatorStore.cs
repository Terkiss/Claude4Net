using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class CoordinatorStore
    {
        private static readonly CoordinatorStore _instance = new();
        public static CoordinatorStore Instance => _instance;

        private CoordinatorStore() { }

        public CoordinateTask CreateTask(string id, string title, string description)
        {
            var task = new CoordinateTask
            {
                Id = id,
                Title = title,
                Description = description,
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow
            };
            
            // Add default gates for Planning phase
            task.Gates.Add(new CoordinateGate { Name = "DesignDoc", IsPassed = false });
            task.Gates.Add(new CoordinateGate { Name = "ResourceCheck", IsPassed = false });

            if (AppState.Tasks.TryAdd(id, task))
            {
                return task;
            }
            throw new InvalidOperationException($"Task with ID '{id}' already exists.");
        }

        public string TransitionPhase(string id, CoordinatePhase nextPhase)
        {
            if (!AppState.Tasks.TryGetValue(id, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{id}' not found.";

            // Validation: Cannot move to Execution if Planning gates are not passed
            if (nextPhase == CoordinatePhase.Execution && task.CurrentPhase == CoordinatePhase.Planning)
            {
                var failedGates = task.Gates.Where(g => !g.IsPassed).ToList();
                if (failedGates.Any())
                {
                    return $"Error: Cannot transition to Execution. Pending gates: {string.Join(", ", failedGates.Select(g => g.Name))}";
                }
            }

            // Validation: Cannot move to Completed without Approval
            if (nextPhase == CoordinatePhase.Completed)
            {
                if (task.ReviewStatus != ReviewerDecision.Approved)
                {
                    return "Error: Cannot complete task without Approved status.";
                }
            }

            var oldPhase = task.CurrentPhase;
            task.CurrentPhase = nextPhase;
            task.Status = nextPhase.ToString();
            task.LastUpdatedAt = DateTime.UtcNow;
            task.History.Add($"Phase transitioned from {oldPhase} to {nextPhase} at {task.LastUpdatedAt}");

            // Auto-add gates for new phases
            if (nextPhase == CoordinatePhase.Execution)
            {
                if (!task.Gates.Any(g => g.Name.Equals("UnitTests", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "UnitTests", IsPassed = false });
                if (!task.Gates.Any(g => g.Name.Equals("LintCheck", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "LintCheck", IsPassed = false });
            }
            else if (nextPhase == CoordinatePhase.Verification)
            {
                if (!task.Gates.Any(g => g.Name.Equals("SecurityAudit", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "SecurityAudit", IsPassed = false });
                if (!task.Gates.Any(g => g.Name.Equals("UserAcceptance", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "UserAcceptance", IsPassed = false });
            }

            return $"Success: Task '{id}' transitioned to {nextPhase}.";
        }

        public string UpdateGate(string taskId, string gateName, bool passed, string? comments = null)
        {
            if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{taskId}' not found.";

            var gate = task.Gates.FirstOrDefault(g => g.Name.Equals(gateName, StringComparison.OrdinalIgnoreCase));
            if (gate == null)
            {
                gate = new CoordinateGate { Name = gateName };
                task.Gates.Add(gate);
            }

            gate.IsPassed = passed;
            gate.Comments = comments;
            gate.UpdatedAt = DateTime.UtcNow;
            task.LastUpdatedAt = DateTime.UtcNow;
            task.History.Add($"Gate '{gateName}' updated to {(passed ? "Passed" : "Failed")} at {gate.UpdatedAt}");

            return $"Success: Gate '{gateName}' updated for task '{taskId}'.";
        }

        public string SetReview(string taskId, ReviewerDecision decision, string? comments = null)
        {
             if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{taskId}' not found.";

             task.ReviewStatus = decision;
             task.LastUpdatedAt = DateTime.UtcNow;
             task.History.Add($"Review status set to {decision} at {task.LastUpdatedAt}. Comments: {comments}");

             return $"Success: Review status for task '{taskId}' set to {decision}.";
        }
    }
}
