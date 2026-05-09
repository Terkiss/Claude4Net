using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K027MultiAgentCoordinationTests
    {
        private readonly ITaskBoard _board;
        private readonly AgentCoordinator _coordinator;
        private readonly MultiAgentOrchestrator _orchestrator;

        public K027MultiAgentCoordinationTests()
        {
            AppState.Tasks.Clear();
            _board = new PandasTaskBoard();
            _coordinator = new AgentCoordinator(_board);
            _orchestrator = new MultiAgentOrchestrator(_board);
        }

        [Fact]
        public void TaskBoard_DependencyOrdering_ShouldOnlyAssignWhenMet()
        {
            // Arrange
            var task1 = new CoordinateTask { Id = "T1", Title = "First" };
            var task2 = new CoordinateTask { Id = "T2", Title = "Second", Dependencies = new List<string> { "T1" } };
            _board.AddTask(task1);
            _board.AddTask(task2);

            // Act
            var pending1 = _board.GetPendingTasks().ToList();
            Assert.Single(pending1);
            Assert.Equal("T1", pending1[0].Id);

            // Complete T1
            task1.CurrentPhase = CoordinatePhase.Completed;
            _board.UpdateTask(task1);

            // Assert
            var pending2 = _board.GetPendingTasks().ToList();
            Assert.Single(pending2);
            Assert.Equal("T2", pending2[0].Id);
        }

        [Fact]
        public void TaskBoard_SpecializationMatching_ShouldRespectRoles()
        {
            // Arrange
            var coderTask = new CoordinateTask { Id = "C1", Title = "Code Fix", RequiredRole = AgentRole.Coder };
            _board.AddTask(coderTask);

            var researcher = new AgentProfile { Name = "Alice", Role = AgentRole.Researcher };
            var coder = new AgentProfile { Name = "Bob", Role = AgentRole.Coder };
            _coordinator.RegisterAgent(researcher);
            _coordinator.RegisterAgent(coder);

            // Act
            int assigned = _coordinator.DispatchTasks();

            // Assert
            Assert.Equal(1, assigned);
            var updatedTask = _board.GetTask("C1");
            Assert.Equal("Bob", updatedTask?.AssignedAgent);
            Assert.True(coder.IsBusy);
            Assert.False(researcher.IsBusy);
        }

        [Fact]
        public void Coordinator_DeadlockDetection_ShouldFailCircularTasks()
        {
            // Arrange
            var t1 = new CoordinateTask { Id = "T1", Title = "Task 1", Dependencies = new List<string> { "T2" } };
            var t2 = new CoordinateTask { Id = "T2", Title = "Task 2", Dependencies = new List<string> { "T1" } };
            _board.AddTask(t1);
            _board.AddTask(t2);

            // Act
            _coordinator.DetectDeadlocks();

            // Assert
            Assert.Equal(CoordinatePhase.Failed, _board.GetTask("T1")?.CurrentPhase);
            Assert.Equal(CoordinatePhase.Failed, _board.GetTask("T2")?.CurrentPhase);
        }

        [Fact]
        public void Orchestrator_Decomposition_ShouldCreateSubtasks()
        {
            // Arrange
            var parent = new CoordinateTask { Id = "P1", Title = "Main Goal" };
            _board.AddTask(parent);

            var steps = new List<(string title, string desc, AgentRole role)>
            {
                ("Research", "Do research", AgentRole.Researcher),
                ("Implement", "Write code", AgentRole.Coder)
            };

            // Act
            _orchestrator.DecomposeGoal("P1", steps);

            // Assert
            var p = _board.GetTask("P1");
            Assert.Equal(2, p?.SubTaskIds.Count);

            var s1 = _board.GetTask("P1_1");
            var s2 = _board.GetTask("P1_2");
            Assert.NotNull(s1);
            Assert.NotNull(s2);
            Assert.Contains("P1_1", s2.Dependencies);
            Assert.Equal(AgentRole.Researcher, s1.RequiredRole);
            Assert.Equal(AgentRole.Coder, s2.RequiredRole);
        }

        [Fact]
        public async Task Handoff_ContextPassing_ShouldAppendSummary()
        {
            // Arrange
            var t1 = new CoordinateTask { Id = "T1", Title = "Task 1" };
            var t2 = new CoordinateTask { Id = "T2", Title = "Task 2" };
            _board.AddTask(t1);
            _board.AddTask(t2);

            // Act
            await _coordinator.RunHandoffAsync("T1", "T2", "Result from T1 is 42");

            // Assert
            var updatedT2 = _board.GetTask("T2");
            Assert.Contains("Result from T1 is 42", updatedT2?.Description);
        }

        [Fact]
        public void E2E_ResearchAndCode_Scenario()
        {
            // 1. Setup agents
            var researcher = new AgentProfile { Name = "ResAgent", Role = AgentRole.Researcher };
            var coder = new AgentProfile { Name = "CodeAgent", Role = AgentRole.Coder };
            _coordinator.RegisterAgent(researcher);
            _coordinator.RegisterAgent(coder);

            // 2. Setup root goal
            var root = new CoordinateTask { Id = "GOAL", Title = "Fix Security Bug" };
            root.AssignedAgent = "Orchestrator"; // Mark root as managed by orchestrator
            _board.AddTask(root);

            // 3. Orchestrator decomposes
            _orchestrator.DecomposeGoal("GOAL", new List<(string, string, AgentRole)>
            {
                ("Analyze", "Find the root cause", AgentRole.Researcher),
                ("Patch", "Apply the fix", AgentRole.Coder)
            });

            // 4. First dispatch (Analyze should be assigned)
            int d1 = _coordinator.DispatchTasks();
            Assert.Equal(1, d1);
            Assert.Equal("ResAgent", _board.GetTask("GOAL_1")?.AssignedAgent);
            Assert.Null(_board.GetTask("GOAL_2")?.AssignedAgent);

            // 5. Complete Analyze
            var t1 = _board.GetTask("GOAL_1")!;
            t1.CurrentPhase = CoordinatePhase.Completed;
            researcher.IsBusy = false;
            _board.UpdateTask(t1);

            // 6. Second dispatch (Patch should now be assigned)
            int d2 = _coordinator.DispatchTasks();
            Assert.Equal(1, d2);
            Assert.Equal("CodeAgent", _board.GetTask("GOAL_2")?.AssignedAgent);
        }
    }
}
