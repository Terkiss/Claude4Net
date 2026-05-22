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
            task.Gates.Add(new CoordinateGate { Name = "DesignDoc", IsPassed = false, IsEvidenceRequired = true });
            task.Gates.Add(new CoordinateGate { Name = "ResourceCheck", IsPassed = false, IsEvidenceRequired = false });

            if (AppState.Tasks.TryAdd(id, task))
            {
                UpdateMergeReadiness(task);
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
                if (task.SpecId != null)
                {
                    SeedSpecRecord? spec = null;
                    if (!string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        string specPath = System.IO.Path.Combine(AppState.CurrentCwd, ".claude4net", "specs", task.SpecId, "seed-spec.json");
                        if (System.IO.File.Exists(specPath))
                        {
                            try
                            {
                                string json = System.IO.File.ReadAllText(specPath);
                                spec = System.Text.Json.JsonSerializer.Deserialize<SeedSpecRecord>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }

                    if (spec == null)
                    {
                        if (task.SpecLockedAt == null)
                        {
                            return $"Error: Cannot transition to Execution. Attached Spec '{task.SpecId}' must be locked first.";
                        }
                    }
                    else
                    {
                        if (spec.Status != SeedSpecStatus.Locked)
                        {
                            return $"Error: Cannot transition to Execution. Attached Spec '{task.SpecId}' must be locked first.";
                        }

                        var unansweredBlocking = spec.OpenQuestions?.Where(q => q.IsBlocking && string.IsNullOrEmpty(q.Answer)).ToList();
                        if (unansweredBlocking != null && unansweredBlocking.Any())
                        {
                            return $"Error: Cannot transition to Execution. Attached Spec '{task.SpecId}' has unanswered blocking questions: {string.Join(", ", unansweredBlocking.Select(q => q.Question))}";
                        }
                    }
                }

                var pendingGates = task.Gates.Where(g => !g.IsPassed).ToList();
                if (pendingGates.Any())
                {
                    return $"Error: Cannot transition to Execution. Pending gates: {string.Join(", ", pendingGates.Select(g => g.Name))}";
                }
            }

            // Validation: Cannot move to Verification if Execution gates are not passed
            if (nextPhase == CoordinatePhase.Verification && task.CurrentPhase == CoordinatePhase.Execution)
            {
                var pendingGates = task.Gates.Where(g => !g.IsPassed).ToList();
                if (pendingGates.Any())
                {
                    return $"Error: Cannot transition to Verification. Pending gates: {string.Join(", ", pendingGates.Select(g => g.Name))}";
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
                    task.Gates.Add(new CoordinateGate { Name = "UnitTests", IsPassed = false, IsEvidenceRequired = true });
                if (!task.Gates.Any(g => g.Name.Equals("LintCheck", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "LintCheck", IsPassed = false, IsEvidenceRequired = false });
            }
            else if (nextPhase == CoordinatePhase.Verification)
            {
                if (!task.Gates.Any(g => g.Name.Equals("SecurityAudit", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "SecurityAudit", IsPassed = false, IsEvidenceRequired = true });
                if (!task.Gates.Any(g => g.Name.Equals("UserAcceptance", StringComparison.OrdinalIgnoreCase)))
                    task.Gates.Add(new CoordinateGate { Name = "UserAcceptance", IsPassed = false, IsEvidenceRequired = true });
            }

            UpdateMergeReadiness(task);
            return $"Success: Task '{id}' transitioned to {nextPhase}.";
        }

        public string AddEvidence(string taskId, string gateName, string author, string summary, string? details = null)
        {
            if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{taskId}' not found.";

            var gate = task.Gates.FirstOrDefault(g => g.Name.Equals(gateName, StringComparison.OrdinalIgnoreCase));
            if (gate == null)
            {
                gate = new CoordinateGate { Name = gateName, IsEvidenceRequired = true };
                task.Gates.Add(gate);
            }

            var evidence = new CoordinateEvidence
            {
                Author = author,
                Phase = task.CurrentPhase,
                GateName = gateName,
                Summary = summary,
                Details = details,
                Timestamp = DateTime.UtcNow
            };

            gate.Evidences.Add(evidence);
            task.LastUpdatedAt = DateTime.UtcNow;
            task.History.Add($"Evidence added to Gate '{gateName}' by {author} at {evidence.Timestamp}");

            UpdateMergeReadiness(task);
            return $"Success: Evidence added to gate '{gateName}' for task '{taskId}'.";
        }

        public string UpdateGate(string taskId, string gateName, bool passed, string? comments = null, string? approvedBy = null)
        {
            if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{taskId}' not found.";

            var gate = task.Gates.FirstOrDefault(g => g.Name.Equals(gateName, StringComparison.OrdinalIgnoreCase));
            if (gate == null)
            {
                gate = new CoordinateGate { Name = gateName };
                task.Gates.Add(gate);
            }

            // Evidence Enforcement
            if (passed && gate.IsEvidenceRequired && !gate.Evidences.Any())
            {
                return $"Error: Gate '{gateName}' requires at least one Evidence record before it can be passed.";
            }

            gate.IsPassed = passed;
            gate.Comments = comments;
            gate.ApprovedBy = approvedBy;
            gate.UpdatedAt = DateTime.UtcNow;
            task.LastUpdatedAt = DateTime.UtcNow;
            task.History.Add($"Gate '{gateName}' updated to {(passed ? "Passed" : "Failed")} by {approvedBy ?? "System"} at {gate.UpdatedAt}");

            UpdateMergeReadiness(task);
            return $"Success: Gate '{gateName}' updated for task '{taskId}'.";
        }

        public void UpdateMergeReadiness(CoordinateTask task)
        {
            task.Blockers.Clear();
            int totalGates = task.Gates.Count;
            int passedGates = task.Gates.Count(g => g.IsPassed);

            double score = 0;

            // Phase based base score
            score = task.CurrentPhase switch
            {
                CoordinatePhase.Planning => 10,
                CoordinatePhase.Execution => 40,
                CoordinatePhase.Verification => 70,
                CoordinatePhase.Completed => 100,
                _ => 0
            };

            // Gate based adjustment
            if (totalGates > 0)
            {
                double gateBonus = (double)passedGates / totalGates * 20;
                score += gateBonus;
            }

            // Review status adjustment
            if (task.ReviewStatus == ReviewerDecision.Approved) score += 10;
            else if (task.ReviewStatus == ReviewerDecision.Rejected) score = Math.Max(0, score - 30);

            task.ReadinessScore = Math.Min(100, score);

            // Blockers identification
            if (task.CurrentPhase != CoordinatePhase.Completed)
            {
                var pendingGates = task.Gates.Where(g => !g.IsPassed).ToList();
                foreach (var g in pendingGates)
                {
                    string reason = g.IsEvidenceRequired && !g.Evidences.Any() ? "(Evidence Required)" : "";
                    task.Blockers.Add($"Gate: {g.Name} {reason}");
                }

                if (task.ReviewStatus != ReviewerDecision.Approved)
                    task.Blockers.Add($"Review: {task.ReviewStatus}");
            }
        }

        public string SetReview(string taskId, ReviewerDecision decision, string? comments = null)
        {
             if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task)
                return $"Error: Task '{taskId}' not found.";

             task.ReviewStatus = decision;
             task.LastUpdatedAt = DateTime.UtcNow;
             task.History.Add($"Review status set to {decision} at {task.LastUpdatedAt}. Comments: {comments}");

             UpdateMergeReadiness(task);
             return $"Success: Review status for task '{taskId}' set to {decision}.";
        }

        public void SyncGatesFromSpec(string taskId, SeedSpecRecord spec)
        {
            if (!AppState.Tasks.TryGetValue(taskId, out var st) || st is not CoordinateTask task) return;

            task.SpecId = spec.Id;
            task.SpecLockedAt = spec.Status == SeedSpecStatus.Locked ? DateTime.UtcNow : null;
            foreach(var ac in spec.AcceptanceCriteria)
            {
                string gateName = "Spec-" + ac.Id;
                if (!task.Gates.Any(x => x.Name == gateName))
                    task.Gates.Add(new CoordinateGate { Name = gateName, IsEvidenceRequired = ac.Required, IsPassed = !ac.Required });
            }
        }
    }
}
