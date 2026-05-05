using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class PathSafetyTests : IDisposable
    {
        private readonly string? _originalCwd;

        public PathSafetyTests()
        {
            _originalCwd = AppState.CurrentCwd;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
        }

        [Fact]
        public void EvaluateSinglePathSafety_ShouldIdentifyWorkspacePath()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string workspace = Path.GetTempPath();
            AppState.CurrentCwd = workspace;
            string testFile = Path.Combine(workspace, "test.txt");

            // Act
            var result = evaluator.EvaluateSinglePathSafety(testFile);

            // Assert
            Assert.Equal(PathSafetyResult.Workspace, result);
        }

        [Fact]
        public void EvaluateSinglePathSafety_ShouldIdentifyOutsidePath()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string tempBase = Path.GetTempPath();
            string workspace = Path.Combine(tempBase, "claude4net_ws_" + Guid.NewGuid());
            Directory.CreateDirectory(workspace);
            try
            {
                AppState.CurrentCwd = workspace;
                string outsideFile = Path.Combine(tempBase, "outside_" + Guid.NewGuid() + ".txt");

                // Act
                var result = evaluator.EvaluateSinglePathSafety(outsideFile);

                // Assert
                Assert.Equal(PathSafetyResult.Outside, result);
            }
            finally
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
        }

        [Fact]
        public void EvaluateSinglePathSafety_ShouldIdentifySafeSystemPath()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string sysBase = AppState.SystemBaseDir;
            string dbPath = Path.Combine(sysBase, "db", "test.db");

            // Act
            var result = evaluator.EvaluateSinglePathSafety(dbPath);

            // Assert
            Assert.Equal(PathSafetyResult.SafeSystem, result);
        }

        [Fact]
        public void EvaluateSinglePathSafety_ShouldIdentifyDangerousSystemPathAsOutside()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string sysBase = AppState.SystemBaseDir;
            string appFile = Path.Combine(sysBase, "Claude4Net.dll");

            // Act
            var result = evaluator.EvaluateSinglePathSafety(appFile);

            // Assert
            Assert.Equal(PathSafetyResult.Outside, result);
        }

        [Fact]
        public void CheckCommandSafety_ShouldIdentifyUnixAbsolutePaths()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string workspace = Path.GetTempPath();
            AppState.CurrentCwd = workspace;
            
            // Unix-style absolute path in command
            var input = new { command = "cat /etc/passwd" };

            // Act
            var result = evaluator.EvaluateInputSafety(input);

            // Assert
            Assert.Equal(PathSafetyResult.Outside, result);
        }

        [Fact]
        public void CheckCommandSafety_ShouldAllowWindowsCliFlags_OnWindows_ShouldBlockOnUnix()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string workspace = Path.GetTempPath();
            AppState.CurrentCwd = workspace;
            
            // Windows-style short flag (/f) should NOT be identified as Outside path
            var input = new { command = "some_tool.exe /f" };

            // Act
            var result = evaluator.EvaluateInputSafety(input);

            // Assert
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            if (isWindows)
            {
                // It should be Workspace (default for safe commands) and definitely not Outside
                Assert.NotEqual(PathSafetyResult.Outside, result);
            }
            else
            {
                // On Unix, /f is considered an absolute path pointing to root, so it is Outside
                Assert.Equal(PathSafetyResult.Outside, result);
            }
        }

        [Fact]
        public void EvaluateInputSafety_ShouldIdentifyOutsideCommand()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            string workspace = Path.GetTempPath();
            AppState.CurrentCwd = workspace;
            
            // On Windows, C:\Windows is definitely outside TempPath
            var input = new { command = @"cat C:\Windows\System32\drivers\etc\hosts" };

            // Act
            var result = evaluator.EvaluateInputSafety(input);

            // Assert
            Assert.Equal(PathSafetyResult.Outside, result);
        }

        [Fact]
        public void EvaluateSinglePathSafety_ShouldIdentifyUnixOutsidePaths()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            // Set CWD to a specific subdirectory, NOT the root /tmp itself
            AppState.CurrentCwd = "/Users/workspace/project";

            // Act & Assert
            // macOS/Linux system paths should be Outside
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateSinglePathSafety("/etc/passwd"));
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateSinglePathSafety("/tmp/secret.txt"));
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateSinglePathSafety("/Users/admin/.ssh/id_rsa"));
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateSinglePathSafety("/var/log/syslog"));
        }

        [Fact]
        public void CheckCommandSafety_ShouldBlockMixedUnixCommands()
        {
            // Arrange
            var evaluator = new PathSafetyEvaluator();
            AppState.CurrentCwd = Path.GetTempPath();

            // Act & Assert
            // Pipe with sensitive path
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateInputSafety(new { command = "ls | grep /etc/shadow" }));

            // Redirect to sensitive path
            Assert.Equal(PathSafetyResult.Outside, evaluator.EvaluateInputSafety(new { command = "echo 'malicious' > /etc/cron.d/hack" }));
        }

        [Fact]
        public async Task ToolOrchestrator_ShouldRequireApprovalForOutsideAccessInYoloMode()
        {
            // Arrange
            var mockApproval = new Mock<IUserApprovalHandler>();
            mockApproval.Setup(m => m.RequestApprovalAsync(It.IsAny<string>(), It.IsAny<string>()))
                        .ReturnsAsync(false); // Deny by default

            var services = new ServiceCollection().BuildServiceProvider();
            var orchestrator = new ToolOrchestrator(new List<ITool>(), mockApproval.Object, services);
            
            var tool = new Mock<ITool>();
            tool.Setup(t => t.Name).Returns("bash");
            orchestrator.AddTool(tool.Object);

            var originalMode = AppState.CurrentPermissionMode;
            try
            {
                AppState.CurrentPermissionMode = PermissionMode.Yolo;
                AppState.CurrentCwd = Path.GetTempPath();
                
                var request = new ToolUseRequest
                {
                    Id = "1",
                    Name = "bash",
                    Input = new { command = @"cat C:\outside_path_that_does_not_exist.txt" }
                };

                // Act
                var result = await orchestrator.ExecuteToolAsync(request, new object());

                // Assert
                Assert.True(result.IsError);
                Assert.Contains("User denied outside-access", result.Content?.ToString() ?? "");
                mockApproval.Verify(m => m.RequestApprovalAsync("bash", It.IsAny<string>()), Times.Once);
            }
            finally
            {
                AppState.CurrentPermissionMode = originalMode;
            }
        }
    }
}
