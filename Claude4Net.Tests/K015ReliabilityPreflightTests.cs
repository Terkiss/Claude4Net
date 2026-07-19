using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public sealed class K015ReliabilityPreflightTests : IDisposable
    {
        private readonly string? _originalCwd = AppState.CurrentCwd;
        private readonly PermissionMode _originalMode = AppState.CurrentPermissionMode;

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.CurrentPermissionMode = _originalMode;
        }

        [Fact]
        public async Task ToolOrchestrator_ShouldBlockWorkspaceOutsideWriteInPromptMode()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "c4n_ws_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            AppState.CurrentCwd = workspace;
            AppState.CurrentPermissionMode = PermissionMode.Prompt;

            var tool = new Mock<ITool>();
            tool.Setup(t => t.Name).Returns("FileWriteTool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("should not execute");

            var services = new ServiceCollection();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool.Object }, null, services.BuildServiceProvider());
            var request = new ToolUseRequest
            {
                Id = "write-outside",
                Name = "FileWriteTool",
                Input = new { file_path = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid().ToString("N") + ".txt"), content = "x" }
            };

            var result = await orchestrator.ExecuteToolAsync(request, new object());

            Assert.True(result.IsError);
            Assert.Contains("outside workspace access is blocked", result.Content?.ToString());
            tool.Verify(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ToolOrchestrator_ShouldDenyPromptSensitiveWorkspaceWriteWithoutApprovalHandler()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "c4n_prompt_ws_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            AppState.CurrentCwd = workspace;
            AppState.CurrentPermissionMode = PermissionMode.Prompt;

            var tool = new Mock<ITool>();
            tool.Setup(t => t.Name).Returns("FileWriteTool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("should not execute");

            var services = new ServiceCollection();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool.Object }, null, services.BuildServiceProvider());
            var request = new ToolUseRequest
            {
                Id = "write-workspace-no-handler",
                Name = "FileWriteTool",
                Input = new { file_path = Path.Combine(workspace, "inside.txt"), content = "x" }
            };

            var result = await orchestrator.ExecuteToolAsync(request, new object());

            Assert.True(result.IsError);
            Assert.Contains("no approval handler is available", result.Content?.ToString());
            tool.Verify(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ToolOrchestrator_ShouldExecutePromptSensitiveWorkspaceWriteWhenApprovalHandlerApproves()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "c4n_prompt_ws_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            AppState.CurrentCwd = workspace;
            AppState.CurrentPermissionMode = PermissionMode.Prompt;

            var approval = new Mock<IUserApprovalHandler>();
            approval.Setup(a => a.RequestApprovalAsync("FileWriteTool", It.IsAny<string>()))
                .ReturnsAsync(true);

            var tool = new Mock<ITool>();
            tool.Setup(t => t.Name).Returns("FileWriteTool");
            tool.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("executed");

            var services = new ServiceCollection();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool.Object }, approval.Object, services.BuildServiceProvider());
            var request = new ToolUseRequest
            {
                Id = "write-workspace-approved",
                Name = "FileWriteTool",
                Input = new { file_path = Path.Combine(workspace, "inside.txt"), content = "x" }
            };

            var result = await orchestrator.ExecuteToolAsync(request, new object());

            Assert.False(result.IsError);
            approval.Verify(a => a.RequestApprovalAsync("FileWriteTool", It.IsAny<string>()), Times.Once);
            tool.Verify(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public void PathSafetyEvaluator_ShouldDetectSymlinkEscapeWhenPlatformSupportsLinks()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "c4n_link_ws_" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(Path.GetTempPath(), "c4n_link_outside_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(outside);
            string link = Path.Combine(workspace, "escape");
            AppState.CurrentCwd = workspace;

            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var result = new PathSafetyEvaluator().EvaluateSinglePathSafety(link);

            Assert.Equal(PathSafetyResult.Outside, result);
        }

        [Fact]
        public void PathSafetyEvaluator_ShouldDetectSymlinkEscapeForNewFileUnderLinkedDirectoryWhenPlatformSupportsLinks()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "c4n_link_ws_" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(Path.GetTempPath(), "c4n_link_outside_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            Directory.CreateDirectory(outside);
            string link = Path.Combine(workspace, "escape");
            AppState.CurrentCwd = workspace;

            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string newFileUnderLink = Path.Combine(link, "newfile.txt");
            var result = new PathSafetyEvaluator().EvaluateSinglePathSafety(newFileUnderLink);

            Assert.Equal(PathSafetyResult.Outside, result);
        }

        [Fact]
        public void CommandRiskClassifier_ShouldFlagDangerousCommands()
        {
            var classifier = new CommandRiskClassifier();

            var result = classifier.Classify("rm -rf /tmp/example");

            Assert.Equal(CommandRiskLevel.Dangerous, result.Level);
            Assert.True(result.RequiresApproval);
            Assert.Contains("delete", result.Reason);
        }

        [Fact]
        public async Task DoctorCommand_ShouldReturnMachineReadableJson()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISmartRouter>(new SmartRouter());
            var sp = services.BuildServiceProvider();

            var command = CommandRegistry.FindCommand("doctor");
            Assert.NotNull(command);

            string result = await command.Handler!("--output-format json", sp);
            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;

            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.True(root.TryGetProperty("permissionMode", out _));
            Assert.True(root.TryGetProperty("normalizedPermissionMode", out _));
            Assert.True(root.TryGetProperty("apiKeys", out _));
            Assert.DoesNotContain("[bold", result);
        }
    }
}
