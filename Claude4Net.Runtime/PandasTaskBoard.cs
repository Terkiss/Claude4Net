using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class PandasTaskBoard : ITaskBoard
    {
        public void AddTask(CoordinateTask task)
        {
            if (!AppState.Tasks.TryAdd(task.Id, task))
            {
                throw new InvalidOperationException($"Task with ID '{task.Id}' already exists.");
            }
        }

        public CoordinateTask? GetTask(string taskId)
        {
            if (AppState.Tasks.TryGetValue(taskId, out var st) && st is CoordinateTask ct)
            {
                return ct;
            }
            return null;
        }

        public IEnumerable<CoordinateTask> GetPendingTasks()
        {
            return AppState.Tasks.Values
                .OfType<CoordinateTask>()
                .Where(t => t.AssignedAgent == null &&
                            t.CurrentPhase != CoordinatePhase.Completed &&
                            t.CurrentPhase != CoordinatePhase.Failed)
                .Where(t => AreDependenciesMet(t));
        }

        private bool AreDependenciesMet(CoordinateTask task)
        {
            if (task.Dependencies == null || !task.Dependencies.Any()) return true;

            foreach (var depId in task.Dependencies)
            {
                var depTask = GetTask(depId);
                if (depTask == null || depTask.CurrentPhase != CoordinatePhase.Completed)
                {
                    return false;
                }
            }
            return true;
        }

        public void UpdateTask(CoordinateTask task)
        {
            AppState.Tasks[task.Id] = task;
        }

        public bool TryAssignTask(string taskId, string agentName)
        {
            var task = GetTask(taskId);
            if (task == null || task.AssignedAgent != null) return false;

            task.AssignedAgent = agentName;
            task.LastUpdatedAt = DateTime.UtcNow;
            task.History.Add($"Task assigned to agent '{agentName}' at {task.LastUpdatedAt}");
            return true;
        }

        public void DecomposeTask(string parentTaskId, List<CoordinateTask> subTasks)
        {
            var parent = GetTask(parentTaskId);
            if (parent == null) throw new ArgumentException("Parent task not found.");

            foreach (var sub in subTasks)
            {
                sub.ParentTaskId = parentTaskId;
                AddTask(sub);
                parent.SubTaskIds.Add(sub.Id);
            }

            parent.History.Add($"Task decomposed into {subTasks.Count} subtasks.");
            parent.LastUpdatedAt = DateTime.UtcNow;
        }
    }
}
