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
    public class K081DashboardTypedCommandTests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly string _originalCwd;
        private readonly string _testSessionId;
        private readonly string _originalSessionId;
        private readonly PermissionMode _originalPermissionMode;

        public K081DashboardTypedCommandTests()
        {
            _originalCwd = AppState.CurrentCwd ?? string.Empty;
            _originalPermissionMode = AppState.CurrentPermissionMode;
            _originalSessionId = AppState.SessionId;
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K081_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
            _testSessionId = "test-session-456";
            AppState.SessionId = _testSessionId;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.CurrentPermissionMode = _originalPermissionMode;
            AppState.SessionId = _originalSessionId;
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
        public async Task RunRoutine_ShouldExecuteAndReturnSuccess()
        {
            var store = new RoutineStore(_tempWorkspace);
            var routine = new RoutineDefinition
            {
                Id = "routine-k081",
                Name = "Test Routine",
                Enabled = true,
                RequiredPermissionMode = PermissionMode.ReadOnly,
                Trigger = new RoutineTrigger
                {
                    Kind = RoutineTriggerKind.Manual
                },
                Actions = new List<RoutineAction>
                {
                    new RoutineAction
                    {
                        Kind = RoutineActionKind.SlashCommand,
                        Payload = "/help"
                    }
                }
            };
            await store.SaveAsync(routine);

            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var hub = new ControlPlaneHub();
            var result = await hub.RunRoutine("routine-k081");

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Contains("executed successfully", result.Message);
        }

        [Fact]
        public async Task RunRoutine_WithInvalidId_ShouldReturnError()
        {
            var hub = new ControlPlaneHub();
            var result = await hub.RunRoutine("../invalid-id");

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Equal("Invalid Routine ID format.", result.Error);
        }

        [Fact]
        public async Task RestoreCheckpoint_ShouldRestoreAndReturnSuccess_WhenPermissionAllows()
        {
            // Setup a mock checkpoint manifest file
            var store = new CheckpointStore(_tempWorkspace, _testSessionId);
            var dummyFile = Path.Combine(_tempWorkspace, "dummy.txt");
            await File.WriteAllTextAsync(dummyFile, "Original Content");

            AppState.ActiveProvider = "gemini";
            AppState.ActiveModel = "gemini-flash";

            var checkpointId = await store.CreateCheckpointAsync(
                toolCallId: "call_123",
                toolName: "file_write",
                files: new List<string> { "dummy.txt" },
                description: "Initial state checkpoint",
                includeMemoryState: false
            );

            // Change file content
            await File.WriteAllTextAsync(dummyFile, "Modified Content");

            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var hub = new ControlPlaneHub();
            var result = await hub.RestoreCheckpoint(checkpointId);

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Contains("successfully restored", result.Message);

            // Check if file was restored
            string restoredContent = await File.ReadAllTextAsync(dummyFile);
            Assert.Equal("Original Content", restoredContent);
        }

        [Fact]
        public async Task RestoreCheckpoint_ShouldDeny_WhenReadOnlyMode()
        {
            AppState.CurrentPermissionMode = PermissionMode.ReadOnly;

            var hub = new ControlPlaneHub();
            var result = await hub.RestoreCheckpoint("somecheckpoint");

            Assert.NotNull(result);
            Assert.False(result.Success);
            Assert.Contains("Permission denied", result.Error);
        }

        [Fact]
        public async Task ApproveSkillProposal_ShouldApproveAndReturnSuccess()
        {
            var registry = new SkillRegistryService(_tempWorkspace);
            var proposalService = new SkillProposalService(registry);

            Directory.CreateDirectory(Path.Combine(_tempWorkspace, ".claude4net"));

            var proposal = new SkillProposalRecord
            {
                Id = "PROP-001",
                Title = "Proposal Title",
                Description = "Proposal Description",
                Rationale = "Rationale A is long enough to pass validation rules.",
                ProposedChanges = "public class DummyNewSkill { public void Hello() { System.Console.WriteLine(\"Hello World\"); } }",
                Status = SkillProposalStatus.Proposed,
                TargetPath = "skills/test_skill.cs"
            };
            proposalService.CreateProposal(_tempWorkspace, proposal);
            await proposalService.SaveAsync(_tempWorkspace);

            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var services = new ServiceCollection();
            services.AddSingleton(registry);
            services.AddSingleton(proposalService);
            var provider = services.BuildServiceProvider();

            var hub = new ControlPlaneHub(provider);
            AppState.CurrentCwd = _tempWorkspace;
            var result = await hub.ApproveSkillProposal("PROP-001");

            Assert.NotNull(result);
            Assert.True(result.Success);

            // Reload and verify status
            await proposalService.LoadAsync(_tempWorkspace);
            var updatedProposal = proposalService.GetProposal("PROP-001");
            Assert.NotNull(updatedProposal);
            Assert.Equal(SkillProposalStatus.Approved, updatedProposal.Status);
        }

        [Fact]
        public async Task RejectSkillProposal_ShouldRejectAndReturnSuccess()
        {
            var registry = new SkillRegistryService(_tempWorkspace);
            var proposalService = new SkillProposalService(registry);

            Directory.CreateDirectory(Path.Combine(_tempWorkspace, ".claude4net"));

            var proposal = new SkillProposalRecord
            {
                Id = "PROP-002",
                Title = "Proposal Title 2",
                Description = "Proposal Description 2",
                Rationale = "Rationale B",
                ProposedChanges = "Proposed changes content 2",
                Status = SkillProposalStatus.Proposed,
                TargetPath = "skills/test_skill_2.cs"
            };
            proposalService.CreateProposal(_tempWorkspace, proposal);
            await proposalService.SaveAsync(_tempWorkspace);

            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var services = new ServiceCollection();
            services.AddSingleton(registry);
            services.AddSingleton(proposalService);
            var provider = services.BuildServiceProvider();

            var hub = new ControlPlaneHub(provider);
            AppState.CurrentCwd = _tempWorkspace;
            var result = await hub.RejectSkillProposal("PROP-002", "Bad design choice");

            Assert.NotNull(result);
            Assert.True(result.Success);

            // Reload and verify status and reason
            await proposalService.LoadAsync(_tempWorkspace);
            var updatedProposal = proposalService.GetProposal("PROP-002");
            Assert.NotNull(updatedProposal);
            Assert.Equal(SkillProposalStatus.Rejected, updatedProposal.Status);
            Assert.Equal("Bad design choice", updatedProposal.Metadata["RejectionReason"]);
        }

        [Fact]
        public async Task ApplySkillProposal_ShouldApplyAndReturnSuccess()
        {
            var registry = new SkillRegistryService(_tempWorkspace);
            var proposalService = new SkillProposalService(registry);

            Directory.CreateDirectory(Path.Combine(_tempWorkspace, ".claude4net"));

            var proposal = new SkillProposalRecord
            {
                Id = "PROP-003",
                Title = "Proposal Title 3",
                Description = "Proposal Description 3",
                Rationale = "Rationale C",
                ProposedChanges = "public class DummyNewSkill {}", // valid C# code for build verification
                Status = SkillProposalStatus.Approved,
                TargetPath = "skills/test_skill_3.cs"
            };
            proposalService.CreateProposal(_tempWorkspace, proposal);
            await proposalService.SaveAsync(_tempWorkspace);

            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var services = new ServiceCollection();
            services.AddSingleton(registry);
            services.AddSingleton(proposalService);
            var provider = services.BuildServiceProvider();

            var hub = new ControlPlaneHub(provider);
            AppState.CurrentCwd = _tempWorkspace;
            var result = await hub.ApplySkillProposal("PROP-003");

            Assert.NotNull(result);
            Assert.True(result.Success);

            // Check if file was created
            string createdFilePath = Path.Combine(_tempWorkspace, "skills/test_skill_3.cs");
            Assert.True(File.Exists(createdFilePath));
        }

        [Fact]
        public async Task RunVerification_ShouldExecuteAndReturnVerdict()
        {
            AppState.CurrentPermissionMode = PermissionMode.WorkspaceWrite;

            var hub = new ControlPlaneHub();

            // Note: verification runs build & tests, which might fail or pass depending on the workspace.
            // But we should check that it executes and outputs a structured CommandResult.
            var result = await hub.RunVerification(_testSessionId);

            Assert.NotNull(result);
            // Since we're executing in a temp workspace without csproj or source files, build will fail, so success should be false.
            Assert.False(result.Success);
            Assert.Contains("Verification failed", result.Error);
        }
    }
}
