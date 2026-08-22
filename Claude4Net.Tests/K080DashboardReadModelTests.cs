using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Dashboard.Client.Models;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Xunit;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K080DashboardReadModelTests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly string _originalCwd;
        private readonly string _testSessionId;

        public K080DashboardReadModelTests()
        {
            _originalCwd = AppState.CurrentCwd ?? string.Empty;
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K080_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
            _testSessionId = "test-session-" + Guid.NewGuid().ToString("N")[..8];
            AppState.SessionId = _testSessionId;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            try
            {
                if (Directory.Exists(_tempWorkspace))
                {
                    Directory.Delete(_tempWorkspace, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task GetProviders_ShouldReturnPopulatedDescriptors()
        {
            var hub = new ControlPlaneHub();
            var state = await hub.GetProviders();

            Assert.NotNull(state);
            Assert.NotNull(state.Providers);
            Assert.NotEmpty(state.Providers);

            var firstProvider = state.Providers.First();
            Assert.False(string.IsNullOrWhiteSpace(firstProvider.Id));
            Assert.False(string.IsNullOrWhiteSpace(firstProvider.Label));
        }

        [Fact]
        public async Task GetCoordinateTasks_ShouldReturnTasksFromAppState()
        {
            // Clear current tasks for clean state
            AppState.Tasks.Clear();

            var task = new CoordinateTask
            {
                Id = "task-k080",
                Title = "Test Coordinate Task",
                Description = "Verify read model",
                ReadinessScore = 85.5,
                SpecId = "spec-abc",
                Blockers = new List<string> { "Blocker A" }
            };
            task.Gates.Add(new CoordinateGate
            {
                Name = "Gate A",
                IsPassed = true,
                IsEvidenceRequired = false,
                Comments = "Passed easily",
                ApprovedBy = "reviewer-1"
            });

            AppState.Tasks.TryAdd(task.Id, task);

            var hub = new ControlPlaneHub();
            var state = await hub.GetCoordinateTasks();

            Assert.NotNull(state);
            Assert.Single(state.Tasks);

            var resultTask = state.Tasks.First();
            Assert.Equal("task-k080", resultTask.Id);
            Assert.Equal("Test Coordinate Task", resultTask.Title);
            Assert.Equal("Verify read model", resultTask.Description);
            Assert.Equal(85.5, resultTask.ReadinessScore);
            Assert.Equal("spec-abc", resultTask.SpecId);
            Assert.Contains("Blocker A", resultTask.Blockers);

            Assert.Single(resultTask.Gates);
            var resultGate = resultTask.Gates.First();
            Assert.Equal("Gate A", resultGate.Name);
            Assert.True(resultGate.IsPassed);
            Assert.False(resultGate.IsEvidenceRequired);
            Assert.Equal("Passed easily", resultGate.Comments);
            Assert.Equal("reviewer-1", resultGate.ApprovedBy);

            AppState.Tasks.Clear();
        }

        [Fact]
        public async Task GetCheckpoints_WithInvalidSessionId_ShouldReturnEmptyState()
        {
            var hub = new ControlPlaneHub();

            // Session IDs with dangerous traversal characters should be rejected gracefully
            var stateWithDotDot = await hub.GetCheckpoints("../escape");
            var stateWithSlash = await hub.GetCheckpoints("escape/dir");
            var stateWithBackslash = await hub.GetCheckpoints("escape\\dir");
            var stateWithColon = await hub.GetCheckpoints("C:escape");

            Assert.Empty(stateWithDotDot.Checkpoints);
            Assert.Empty(stateWithSlash.Checkpoints);
            Assert.Empty(stateWithBackslash.Checkpoints);
            Assert.Empty(stateWithColon.Checkpoints);
        }

        [Fact]
        public async Task GetCheckpoints_WithValidSessionId_ShouldReturnCheckpoints()
        {
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;

            // Setup a mock checkpoint manifest file
            var store = new CheckpointStore(_tempWorkspace, _testSessionId);
            var dummyFile = Path.Combine(_tempWorkspace, "dummy.txt");
            await File.WriteAllTextAsync(dummyFile, "Hello World");

            // We must simulate AppState settings so provider/model are set
            AppState.ActiveProvider = "gemini";
            AppState.ActiveModel = "gemini-flash";

            var checkpointId = await store.CreateCheckpointAsync(
                toolCallId: "call_abc",
                toolName: "file_write",
                files: new List<string> { "dummy.txt" },
                description: "Test Checkpoint Description",
                includeMemoryState: false
            );

            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;
            var hub = new ControlPlaneHub();
            var state = await hub.GetCheckpoints(_testSessionId);

            Assert.NotNull(state);
            Assert.NotEmpty(state.Checkpoints);

            var cpDto = state.Checkpoints.FirstOrDefault(c => c.Id == checkpointId);
            Assert.NotNull(cpDto);
            Assert.Equal("call_abc", cpDto.ToolCallId);
            Assert.Equal("file_write", cpDto.ToolName);
            Assert.Equal("Test Checkpoint Description", cpDto.Description);
            Assert.Contains("dummy.txt", cpDto.ChangedFiles);
            Assert.Equal("gemini", cpDto.Provider);
            Assert.Equal("gemini-flash", cpDto.Model);
            Assert.False(cpDto.IncludesMemoryState);
        }

        [Fact]
        public async Task GetVerification_WithInvalidSessionId_ShouldReturnEmptyState()
        {
            var hub = new ControlPlaneHub();

            var stateWithDotDot = await hub.GetVerification("../escape");
            Assert.Null(stateWithDotDot.Result);
        }

        [Fact]
        public async Task GetVerification_WithValidSessionId_ShouldReturnVerificationResult()
        {
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;
            var sessionStore = new AgentSessionStore(_tempWorkspace, _testSessionId);

            var check = new VerificationCheck
            {
                Name = "Compile Check",
                Command = "dotnet build",
                OutputFile = "build.log",
                Result = VerificationVerdict.Pass,
                Evidence = "Build succeeded.",
                Notes = "No warnings",
                Skipped = false,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt = DateTimeOffset.UtcNow
            };

            var verificationResult = new VerificationResult
            {
                VerifierSessionId = "verifier-session-xyz",
                GeneratorSessionId = _testSessionId,
                Verdict = VerificationVerdict.Pass,
                Checks = new List<VerificationCheck> { check },
                Timestamp = DateTimeOffset.UtcNow
            };

            await sessionStore.SaveVerificationResultAsync(verificationResult);

            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;
            var hub = new ControlPlaneHub();
            var state = await hub.GetVerification(_testSessionId);

            Assert.NotNull(state);
            Assert.NotNull(state.Result);
            Assert.Equal("verifier-session-xyz", state.Result.VerifierSessionId);
            Assert.Equal(_testSessionId, state.Result.GeneratorSessionId);
            Assert.Equal("Pass", state.Result.Verdict);
            Assert.Single(state.Result.Checks);

            var checkDto = state.Result.Checks.First();
            Assert.Equal("Compile Check", checkDto.Name);
            Assert.Equal("dotnet build", checkDto.Command);
            Assert.Equal("build.log", checkDto.OutputFile);
            Assert.Equal("Pass", checkDto.Result);
            Assert.Equal("Build succeeded.", checkDto.Evidence);
            Assert.Equal("No warnings", checkDto.Notes);
            Assert.False(checkDto.Skipped);
            Assert.NotNull(checkDto.StartedAt);
            Assert.NotNull(checkDto.CompletedAt);
        }

        [Fact]
        public async Task GetSkills_ShouldReturnRegisteredSkillsAndProposals()
        {
            // Register a skill using SkillRegistryService
            var registry = new SkillRegistryService(_tempWorkspace);
            var skillRecord = new SkillRegistryRecord
            {
                Id = "skill-k080",
                DisplayName = "Test Skill",
                SourcePath = "skills/test_skill.md",
                Aliases = new List<string> { "test" },
                Metrics = new SkillQualityMetrics
                {
                    SuccessCount = 10,
                    FailureCount = 2,
                    AverageScore = 4.5,
                    LastUsed = DateTime.UtcNow
                }
            };

            Directory.CreateDirectory(Path.Combine(_tempWorkspace, ".claude4net"));
            registry.RegisterSkill(skillRecord);
            await registry.SaveAsync();

            // Add a skill proposal
            var proposalService = new SkillProposalService(registry);
            var proposal = new SkillProposalRecord
            {
                Id = "proposal-abc",
                SkillId = "skill-k080",
                Title = "Proposal Title",
                Description = "Proposal Description",
                Rationale = "Rationale A",
                ProposedChanges = "Proposed changes content",
                Status = SkillProposalStatus.Proposed,
                TargetPath = "skills/test_skill.md",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            proposalService.CreateProposal(_tempWorkspace, proposal);
            await proposalService.SaveAsync(_tempWorkspace);

            // Setup ControlPlaneHub with ServiceProvider providing these custom services
            var services = new ServiceCollection();
            services.AddSingleton(registry);
            services.AddSingleton(proposalService);
            var provider = services.BuildServiceProvider();

            var hub = new ControlPlaneHub(provider);
            AppState.CurrentCwd = _tempWorkspace;
            var state = await hub.GetSkills();

            Assert.NotNull(state);

            var skillDto = state.Skills.FirstOrDefault(s => s.Id == "skill-k080");
            Assert.NotNull(skillDto);
            Assert.Equal("Test Skill", skillDto.DisplayName);
            Assert.Equal("skills/test_skill.md", skillDto.SourcePath);
            Assert.Contains("test", skillDto.Aliases);
            Assert.Equal(10, skillDto.Metrics.SuccessCount);
            Assert.Equal(2, skillDto.Metrics.FailureCount);
            Assert.Equal(4.5, skillDto.Metrics.AverageScore);

            var proposalDto = state.Proposals.FirstOrDefault(p => p.Id == "proposal-abc");
            Assert.NotNull(proposalDto);
            Assert.Equal("skill-k080", proposalDto.SkillId);
            Assert.Equal("Proposal Title", proposalDto.Title);
            Assert.Equal("Proposal Description", proposalDto.Description);
            Assert.Equal("Rationale A", proposalDto.Rationale);
            Assert.Equal("Proposed", proposalDto.Status); // Enum conversion to string is "Proposed"
            Assert.Equal("skills/test_skill.md", proposalDto.TargetPath);
        }

        [Fact]
        public async Task GetRoutines_ShouldReturnConfiguredRoutines()
        {
            var store = new RoutineStore(_tempWorkspace);
            var routine = new RoutineDefinition
            {
                Id = "routine-k080",
                Name = "Clean Routine",
                Enabled = true,
                RequiredPermissionMode = PermissionMode.ReadOnly,
                Trigger = new RoutineTrigger
                {
                    Kind = RoutineTriggerKind.Interval,
                    Expression = "60"
                },
                Actions = new List<RoutineAction>
                {
                    new RoutineAction
                    {
                        Kind = RoutineActionKind.SlashCommand,
                        Payload = "{\"/routine list\"}"
                    }
                }
            };
            await store.SaveAsync(routine);

            var runRecord = new RoutineRunRecord
            {
                RunId = "run-111",
                RoutineId = "routine-k080",
                Success = true,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                CompletedAt = DateTime.UtcNow.AddMinutes(-4),
                Error = null
            };
            await store.SaveRunRecordAsync(runRecord);

            AppState.CurrentCwd = _tempWorkspace;
            var hub = new ControlPlaneHub();
            var state = await hub.GetRoutines();

            Assert.NotNull(state);
            Assert.NotEmpty(state.Routines);

            var routineDto = state.Routines.FirstOrDefault(r => r.Id == "routine-k080");
            Assert.NotNull(routineDto);
            Assert.Equal("Clean Routine", routineDto.Name);
            Assert.True(routineDto.Enabled);
            Assert.Equal("ReadOnly", routineDto.RequiredPermissionMode);
            Assert.Equal("Interval", routineDto.TriggerKind);
            Assert.Equal("60", routineDto.TriggerExpression);
            Assert.Equal("Success", routineDto.LastRunStatus);
            Assert.Single(routineDto.Actions);
            Assert.Equal("SlashCommand", routineDto.Actions[0].Type);

            Assert.Single(routineDto.History);
            Assert.Equal("run-111", routineDto.History[0].RunId);
            Assert.Equal("Success", routineDto.History[0].Status);
        }

        [Fact]
        public async Task GetState_WithInvalidSessionId_ShouldReturnEmptyState()
        {
            var hub = new ControlPlaneHub();

            var stateWithDotDot = await hub.GetState("../escape");
            Assert.Null(stateWithDotDot.Session);
            Assert.Null(stateWithDotDot.TaskBoard);
            Assert.Empty(stateWithDotDot.MemoryTables);
        }

        [Fact]
        public async Task GetState_WithValidSessionId_ShouldReturnSessionAndTaskBoard()
        {
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;

            // 1. Session Setup
            var sessionRecord = new AgentSessionRecord
            {
                SessionId = _testSessionId,
                StartTime = DateTime.UtcNow,
                Provider = "gemini",
                Model = "gemini-3.1-flash",
                PermissionMode = PermissionMode.WorkspaceWrite,
                WorkspacePath = _tempWorkspace,
                Status = "Running",
                Metadata = new Dictionary<string, string> { { "Key", "Value" } }
            };

            var sessionStore = new AgentSessionStore(_tempWorkspace, _testSessionId);
            await sessionStore.InitializeAsync(sessionRecord);

            // 2. TaskBoard Setup
            var taskBoard = new AgentTaskBoardRecord
            {
                SessionId = _testSessionId,
                LastUpdatedAt = DateTime.UtcNow,
                Tasks = new List<AgentTaskRecord>
                {
                    new AgentTaskRecord
                    {
                        Id = "subtask-1",
                        Title = "Verify DB",
                        Description = "Test database read",
                        Status = "Pending",
                        AssignedAgent = "coder-agent",
                        Progress = 50.0,
                        Dependencies = new List<string> { "pretask-1" }
                    }
                }
            };
            await sessionStore.SaveTaskBoardAsync(taskBoard);

            // 3. Memory Database Setup
            var targetContext = new WorkspaceStateContext
            {
                WorkspaceRoot = _tempWorkspace,
                SessionId = _testSessionId
            };
            var universeStore = PandasUniverseManager.Instance.GetStore(targetContext);
            await universeStore.ExecuteAsync(u =>
            {
                PandasUniverseManager.EnsureBaselineTablesInternal(u);
            });

            var hub = new ControlPlaneHub();
            var state = await hub.GetState(_testSessionId);

            Assert.NotNull(state);
            Assert.NotNull(state.Session);
            Assert.Equal(_testSessionId, state.Session.SessionId);
            Assert.Equal("gemini", state.Session.Provider);
            Assert.Equal("gemini-3.1-flash", state.Session.Model);
            Assert.Equal("WorkspaceWrite", state.Session.PermissionMode);
            Assert.Equal("Running", state.Session.Status);
            Assert.Equal("Value", state.Session.Metadata["Key"]);

            Assert.NotNull(state.TaskBoard);
            Assert.Equal(_testSessionId, state.TaskBoard.SessionId);
            Assert.Single(state.TaskBoard.Tasks);

            var taskDto = state.TaskBoard.Tasks.First();
            Assert.Equal("subtask-1", taskDto.Id);
            Assert.Equal("Verify DB", taskDto.Title);
            Assert.Equal("coder-agent", taskDto.AssignedAgent);
            Assert.Equal(50.0, taskDto.Progress);
            Assert.Contains("pretask-1", taskDto.Dependencies);

            // Memory Tables assertion
            Assert.NotEmpty(state.MemoryTables);
            Assert.Contains(state.MemoryTables, t => t.Name == "agent_memory");
        }
    }
}
