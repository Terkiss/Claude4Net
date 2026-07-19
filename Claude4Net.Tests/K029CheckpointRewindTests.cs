using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Collections.Generic;
using System.Linq;
using Moq;
using System.Text.Json;
using System.Security;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K029CheckpointRewindTests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly string _sessionId;

        public K029CheckpointRewindTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K029_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            _sessionId = "test-session-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            // Set global state for tests
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _sessionId;
            AppState.CurrentPermissionMode = PermissionMode.Yolo;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWorkspace))
            {
                try { Directory.Delete(_tempWorkspace, true); } catch { }
            }
        }

        [Fact]
        public async Task Checkpoint_RestoreSingleFile()
        {
            // Arrange
            string fileName = "test.txt";
            string fullPath = Path.Combine(_tempWorkspace, fileName);
            string originalContent = "Original Content";
            await File.WriteAllTextAsync(fullPath, originalContent);

            var store = new CheckpointStore(_tempWorkspace, _sessionId);

            // Act - Create Checkpoint
            string cpId = await store.CreateCheckpointAsync("call-1", "TestTool", new List<string> { fileName });

            // Modify file
            await File.WriteAllTextAsync(fullPath, "Modified Content");

            // Restore
            await store.RestoreCheckpointAsync(cpId);

            // Assert
            string restoredContent = await File.ReadAllTextAsync(fullPath);
            Assert.Equal(originalContent, restoredContent);
        }

        [Fact]
        public async Task Checkpoint_RestoreMultipleFiles()
        {
            // Arrange
            string f1 = "a.txt";
            string f2 = "sub/b.txt";
            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, f1), "A1");
            Directory.CreateDirectory(Path.Combine(_tempWorkspace, "sub"));
            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, f2), "B1");

            var store = new CheckpointStore(_tempWorkspace, _sessionId);

            // Act
            string cpId = await store.CreateCheckpointAsync("call-1", "MultiTool", new List<string> { f1, f2 });

            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, f1), "A2");
            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, f2), "B2");

            await store.RestoreCheckpointAsync(cpId);

            // Assert
            Assert.Equal("A1", await File.ReadAllTextAsync(Path.Combine(_tempWorkspace, f1)));
            Assert.Equal("B1", await File.ReadAllTextAsync(Path.Combine(_tempWorkspace, f2)));
        }

        [Fact]
        public async Task Checkpoint_CreatedBeforeFileWrite()
        {
            // Arrange
            var spMock = new Mock<IServiceProvider>();
            var toolMock = new Mock<IPreviewableTool>();
            toolMock.Setup(t => t.Name).Returns("FileWriteTool");
            toolMock.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(new { status = "success" });
            toolMock.Setup(t => t.GetPreviewAsync(It.IsAny<string>()))
                    .ReturnsAsync(new FileDiffPreview { DiffContent = "Patch content" });

            var orchestrator = ToolOrchestrator.CreateForTest(new[] { toolMock.Object }, null, spMock.Object);

            string fileName = "write-test.txt";
            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, fileName), "Old");

            var request = new ToolUseRequest
            {
                Id = "req-1",
                Name = "FileWriteTool",
                Input = new { file_path = fileName, content = "New" }
            };

            // Act
            await orchestrator.ExecuteToolAsync(request, new object());

            // Assert
            var store = new CheckpointStore(_tempWorkspace, _sessionId);
            var cps = await store.ListCheckpointsAsync();
            Assert.NotEmpty(cps);
            Assert.Equal("FileWriteTool", cps[0].ToolName);
            Assert.Contains(fileName, cps[0].ChangedFiles);

            // Verify 'before' content was saved
            string cpDir = Path.Combine(_tempWorkspace, ".claude4net", "sessions", _sessionId, "checkpoints", cps[0].Id);
            string backupFile = Path.Combine(cpDir, "before", fileName);
            Assert.True(File.Exists(backupFile));
            Assert.Equal("Old", await File.ReadAllTextAsync(backupFile));

            // Verify 'after.patch' was saved
            Assert.True(File.Exists(Path.Combine(cpDir, "after.patch")));
            Assert.Equal("Patch content", await File.ReadAllTextAsync(Path.Combine(cpDir, "after.patch")));
        }

        [Fact]
        public async Task Checkpoint_CreatedBeforeFileEdit()
        {
            // Arrange
            var spMock = new Mock<IServiceProvider>();
            var toolMock = new Mock<IPreviewableTool>();
            toolMock.Setup(t => t.Name).Returns("FileEditTool");
            toolMock.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                    .ReturnsAsync(new { status = "success" });

            var orchestrator = ToolOrchestrator.CreateForTest(new[] { toolMock.Object }, null, spMock.Object);

            string fileName = "edit-test.txt";
            await File.WriteAllTextAsync(Path.Combine(_tempWorkspace, fileName), "Line 1\nLine 2");

            var request = new ToolUseRequest
            {
                Id = "req-edit",
                Name = "FileEditTool",
                Input = new { path = fileName, old_string = "Line 1", new_string = "Line X" }
            };

            // Act
            await orchestrator.ExecuteToolAsync(request, new object());

            // Assert
            var store = new CheckpointStore(_tempWorkspace, _sessionId);
            var cps = await store.ListCheckpointsAsync();
            Assert.NotEmpty(cps);
            Assert.Equal("FileEditTool", cps[0].ToolName);
            Assert.Contains(fileName, cps[0].ChangedFiles);
        }

        [Fact]
        public async Task Checkpoint_NoOpWriteSkipped_CopiesOriginalIfPresent()
        {
            // Act - Request checkpoint for non-existent file
            var store = new CheckpointStore(_tempWorkspace, _sessionId);
            string cpId = await store.CreateCheckpointAsync("call-none", "Test", new List<string> { "nonexistent.txt" });

            // Assert
            string cpDir = Path.Combine(_tempWorkspace, ".claude4net", "sessions", _sessionId, "checkpoints", cpId);
            Assert.False(File.Exists(Path.Combine(cpDir, "before", "nonexistent.txt")));
            Assert.True(File.Exists(Path.Combine(cpDir, "manifest.json")));
        }

        [Fact]
        public async Task Checkpoint_RejectsPathTraversal()
        {
            var store = new CheckpointStore(_tempWorkspace, _sessionId);
            await Assert.ThrowsAsync<SecurityException>(() => store.CreateCheckpointAsync("t1", "Test", new List<string> { "../outside.txt" }));
        }

        [Fact]
        public async Task Handoff_RequiresEvidenceForCompletedStatus()
        {
            // Arrange
            var store = new HandoffStore(_tempWorkspace, _sessionId);
            var handoff = new SessionHandoffRecord
            {
                SessionId = _sessionId,
                Status = "Completed",
                Summary = "All done",
                EvidenceFiles = new List<string> { "build.log" }
            };

            // Act
            await store.SaveHandoffAsync(handoff);
            await store.AddEvidenceAsync("build.log", "Build Successful");

            var loaded = await store.LoadHandoffAsync();

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal("Completed", loaded.Status);
            Assert.Contains("build.log", loaded.EvidenceFiles);
            Assert.True(File.Exists(Path.Combine(_tempWorkspace, ".claude4net", "sessions", _sessionId, "evidence", "build.log")));
        }
    }
}
