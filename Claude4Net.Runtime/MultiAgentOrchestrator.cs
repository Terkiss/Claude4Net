using System;
using System.Collections.Generic;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class MultiAgentOrchestrator
    {
        private readonly ITaskBoard _board;

        public MultiAgentOrchestrator(ITaskBoard board)
        {
            _board = board;
        }

        public void DecomposeGoal(string parentId, List<(string title, string desc, AgentRole role)> steps)
        {
            var parent = _board.GetTask(parentId);
            if (parent == null) throw new ArgumentException("Parent task not found.");

            var subtasks = new List<CoordinateTask>();
            string? lastTaskId = null;

            foreach (var step in steps)
            {
                var sub = new CoordinateTask
                {
                    Id = $"{parentId}_{subtasks.Count + 1}",
                    Title = step.title,
                    Description = step.desc,
                    RequiredRole = step.role,
                    CreatedAt = DateTime.UtcNow
                };

                if (lastTaskId != null)
                {
                    sub.Dependencies.Add(lastTaskId);
                }

                subtasks.Add(sub);
                lastTaskId = sub.Id;
            }

            _board.DecomposeTask(parentId, subtasks);
        }
    }
}
