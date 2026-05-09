using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class AgentCoordinator
    {
        private readonly List<AgentProfile> _agents = new();
        private readonly ITaskBoard _board;

        public AgentCoordinator(ITaskBoard board)
        {
            _board = board;
        }

        public void RegisterAgent(AgentProfile agent)
        {
            if (!_agents.Any(a => a.Name == agent.Name))
            {
                _agents.Add(agent);
            }
        }

        public List<AgentProfile> GetAgents() => _agents;

        public int DispatchTasks()
        {
            int assignments = 0;
            var pendingTasks = _board.GetPendingTasks().ToList();
            var availableAgents = _agents.Where(a => !a.IsBusy).ToList();

            foreach (var task in pendingTasks)
            {
                var candidate = availableAgents
                    .FirstOrDefault(a => a.Role == task.RequiredRole || task.RequiredRole == AgentRole.Worker);

                if (candidate != null)
                {
                    if (_board.TryAssignTask(task.Id, candidate.Name))
                    {
                        candidate.IsBusy = true;
                        availableAgents.Remove(candidate);
                        assignments++;
                    }
                }
            }

            return assignments;
        }

        public void DetectDeadlocks()
        {
            var pendingTasks = AppState.Tasks.Values.OfType<CoordinateTask>()
                .Where(t => t.CurrentPhase != CoordinatePhase.Completed && t.CurrentPhase != CoordinatePhase.Failed)
                .ToList();

            foreach (var task in pendingTasks)
            {
                if (HasCircularDependency(task.Id, new HashSet<string>()))
                {
                    task.CurrentPhase = CoordinatePhase.Failed;
                    task.History.Add("Circular dependency detected. Task failed.");
                    _board.UpdateTask(task);
                }
            }
        }

        private bool HasCircularDependency(string taskId, HashSet<string> visited)
        {
            if (visited.Contains(taskId)) return true;
            visited.Add(taskId);

            var task = _board.GetTask(taskId);
            if (task?.Dependencies == null) return false;

            foreach (var depId in task.Dependencies)
            {
                if (HasCircularDependency(depId, new HashSet<string>(visited)))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<string> RunHandoffAsync(string fromTaskId, string toTaskId, string summary)
        {
            var fromTask = _board.GetTask(fromTaskId);
            var toTask = _board.GetTask(toTaskId);

            if (fromTask == null || toTask == null) return "Error: Task not found.";

            toTask.Description += $"\n\n[Handoff from {fromTaskId}]\n{summary}";
            toTask.History.Add($"Handoff received from {fromTaskId}");
            _board.UpdateTask(toTask);

            return "Success: Handoff completed.";
        }
    }
}
