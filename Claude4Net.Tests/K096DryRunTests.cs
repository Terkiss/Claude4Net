using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class K096DryRunTests : IDisposable
    {
        private readonly string _tempTestDir;
        private readonly string? _originalCwd;

        public K096DryRunTests()
        {
            _originalCwd = AppState.CurrentCwd;
            _tempTestDir = Path.Combine(Path.GetTempPath(), "K096Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestDir);
            AppState.CurrentCwd = _tempTestDir;
            DryRunEngine.Clear();
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            DryRunEngine.Clear();
            DryRunEngine.IsActive = false;
            if (Directory.Exists(_tempTestDir))
            {
                Directory.Delete(_tempTestDir, true);
            }
        }

        [Fact]
        public async Task DryRun_Active_BlocksRealFileWrite_AndSavesVirtual()
        {
            // Arrange
            DryRunEngine.IsActive = true;
            string testFilePath = Path.Combine(_tempTestDir, "test_write.txt");
            string testContent = "Hello Dry-Run!";

            var services = new ServiceCollection();
            var serviceProvider = services.BuildServiceProvider();

            var writeTool = new FileWriteTool();
            var coreTools = new List<ITool> { writeTool };
            var orchestrator = ToolOrchestrator.CreateForTest(coreTools, null, serviceProvider);

            var request = new ToolUseRequest
            {
                Id = "call_write_1",
                Name = "filewritetool",
                Input = new Dictionary<string, object>
                {
                    { "file_path", testFilePath },
                    { "content", testContent }
                }
            };

            // Act
            var result = await DryRunEngine.ExecuteSimulatedToolAsync(request, orchestrator, CancellationToken.None);

            // Assert
            Assert.False(result.IsError);
            Assert.False(File.Exists(testFilePath), "Real file should not be created in dry-run mode.");

            var report = DryRunEngine.GetReport();
            Assert.Single(report.ToolCalls);
            Assert.Single(report.FileChanges);
            Assert.Equal("Create", report.FileChanges[0].ChangeType);
            Assert.Equal(testFilePath, report.FileChanges[0].FilePath);
        }

        [Fact]
        public async Task DryRun_VirtualReadConsistency_Works()
        {
            // Arrange
            DryRunEngine.IsActive = true;
            string testFilePath = Path.Combine(_tempTestDir, "test_read.txt");
            string testContent = "Line 1\nLine 2\nLine 3";

            var services = new ServiceCollection();
            var serviceProvider = services.BuildServiceProvider();

            var writeTool = new FileWriteTool();
            var readTool = new FileReadTool();
            var coreTools = new List<ITool> { writeTool, readTool };
            var orchestrator = ToolOrchestrator.CreateForTest(coreTools, null, serviceProvider);

            // Write virtually first
            var writeRequest = new ToolUseRequest
            {
                Id = "call_write_2",
                Name = "filewritetool",
                Input = new Dictionary<string, object>
                {
                    { "file_path", testFilePath },
                    { "content", testContent }
                }
            };
            await DryRunEngine.ExecuteSimulatedToolAsync(writeRequest, orchestrator, CancellationToken.None);

            // Act: Read the file that has not been written to real disk but exists virtually
            var readRequest = new ToolUseRequest
            {
                Id = "call_read_2",
                Name = "filereadtool",
                Input = new Dictionary<string, object>
                {
                    { "file_path", testFilePath },
                    { "offset", 2 },
                    { "limit", 2 }
                }
            };

            var readResult = await DryRunEngine.ExecuteSimulatedToolAsync(readRequest, orchestrator, CancellationToken.None);

            // Assert
            Assert.False(readResult.IsError);
            Assert.NotNull(readResult.Content);

            // Read tool return properties check (filePath, content, totalLines)
            var json = System.Text.Json.JsonSerializer.Serialize(readResult.Content);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal("Line 2\nLine 3", doc.RootElement.GetProperty("content").GetString());
            Assert.Equal(3, doc.RootElement.GetProperty("totalLines").GetInt32());
        }
    }
}
