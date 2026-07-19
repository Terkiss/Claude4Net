using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.Tools;
using Moq;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K017DiffTests : IDisposable
    {
        private readonly string _tempWorkspace;

        public K017DiffTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_DiffTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
            AppState.CurrentPermissionMode = PermissionMode.Prompt;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, true);
            }
            AppState.CurrentCwd = null;
        }

        [Fact]
        public void DiffService_GeneratesCorrectUnifiedDiff()
        {
            // Arrange
            string oldText = "Line 1\nLine 2\nLine 3";
            string newText = "Line 1\nLine 2 Modified\nLine 3\nLine 4";
            string path = "test.txt";

            // Act
            string diff = DiffService.GenerateUnifiedDiff(oldText, newText, path);

            // Assert
            Assert.Contains("--- test.txt (original)", diff);
            Assert.Contains("+++ test.txt (proposed)", diff);
            Assert.Contains("- Line 2", diff);
            Assert.Contains("+ Line 2 Modified", diff);
            Assert.Contains("+ Line 4", diff);
        }

        [Fact]
        public async Task FileWriteTool_ProvidesPreviewForNewFile()
        {
            // Arrange
            var tool = new FileWriteTool();
            string filePath = Path.Combine(_tempWorkspace, "newfile.txt");
            string content = "Hello World";
            string args = $"{{\"file_path\": \"{filePath.Replace("\\", "\\\\")}\", \"content\": \"{content}\"}}";

            // Act
            var preview = await tool.GetPreviewAsync(args);

            // Assert
            Assert.NotNull(preview);
            Assert.Equal(filePath, preview.FilePath);
            Assert.Equal(FileChangeType.Create, preview.ChangeType);
            Assert.Contains("+ Hello World", preview.DiffContent);
        }

        [Fact]
        public async Task ToolOrchestrator_UsesRichApprovalHandler()
        {
            // Arrange
            var mockHandler = new Mock<IRichApprovalHandler>();
            mockHandler.Setup(h => h.RequestApprovalWithDiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileDiffPreview>()))
                       .ReturnsAsync(true);

            var tool = new FileWriteTool();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool }, mockHandler.Object, new ServiceCollection().BuildServiceProvider());

            string filePath = Path.Combine(_tempWorkspace, "test.txt");
            var request = new ToolUseRequest
            {
                Id = "call-1",
                Name = "FileWriteTool",
                Input = new { file_path = filePath, content = "Validated Content" }
            };

            // Act
            var result = await orchestrator.ExecuteToolAsync(request, new { });

            // Assert
            Assert.False(result.IsError);
            mockHandler.Verify(h => h.RequestApprovalWithDiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileDiffPreview>()), Times.Once);
            Assert.True(File.Exists(filePath));
            Assert.Equal("Validated Content", File.ReadAllText(filePath));
        }

        [Fact]
        public async Task ToolOrchestrator_BlocksOnDeny()
        {
            // Arrange
            var mockHandler = new Mock<IRichApprovalHandler>();
            mockHandler.Setup(h => h.RequestApprovalWithDiffAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileDiffPreview>()))
                       .ReturnsAsync(false);

            var tool = new FileWriteTool();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool }, mockHandler.Object, new ServiceCollection().BuildServiceProvider());

            string filePath = Path.Combine(_tempWorkspace, "denied.txt");
            var request = new ToolUseRequest
            {
                Id = "call-1",
                Name = "FileWriteTool",
                Input = new { file_path = filePath, content = "Should not exist" }
            };

            // Act
            var result = await orchestrator.ExecuteToolAsync(request, new { });

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("User denied permission", (string)result.Content!);
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public async Task ToolOrchestrator_DeniesByDefaultWhenNoHandler()
        {
            // Arrange
            var tool = new FileWriteTool();
            var orchestrator = ToolOrchestrator.CreateForTest(new[] { tool }, null, new ServiceCollection().BuildServiceProvider());

            string filePath = Path.Combine(_tempWorkspace, "no-handler.txt");
            var request = new ToolUseRequest
            {
                Id = "call-1",
                Name = "FileWriteTool",
                Input = new { file_path = filePath, content = "Fail fast" }
            };

            // Act
            var result = await orchestrator.ExecuteToolAsync(request, new { });

            // Assert
            Assert.True(result.IsError);
            Assert.Contains("no approval handler is available", (string)result.Content!);
            Assert.False(File.Exists(filePath));
        }
    }
}
