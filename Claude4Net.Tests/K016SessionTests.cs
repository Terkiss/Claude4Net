using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System.Collections.Generic;
using System.Linq;

namespace Claude4Net.Tests
{
    public class K016SessionTests : IDisposable
    {
        private readonly string _tempWorkspace;

        public K016SessionTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempWorkspace))
            {
                Directory.Delete(_tempWorkspace, true);
            }
        }

        [Fact]
        public async Task AgentSessionStore_InitializesDirectoryAndSessionJson()
        {
            // Arrange
            string sessionId = "session-123";
            var store = new AgentSessionStore(_tempWorkspace, sessionId);
            var record = new AgentSessionRecord
            {
                SessionId = sessionId,
                Provider = "test-provider",
                Model = "test-model",
                WorkspacePath = _tempWorkspace
            };

            // Act
            await store.InitializeAsync(record);

            // Assert
            string sessionDir = Path.Combine(_tempWorkspace, ".claude4net", "sessions", sessionId);
            Assert.True(Directory.Exists(sessionDir));
            Assert.True(File.Exists(Path.Combine(sessionDir, "session.json")));

            var loadedRecord = await AgentSessionStore.LoadSessionRecordAsync(_tempWorkspace, sessionId);
            Assert.NotNull(loadedRecord);
            Assert.Equal(sessionId, loadedRecord.SessionId);
            Assert.Equal("test-provider", loadedRecord.Provider);
        }

        [Fact]
        public async Task AgentSessionStore_SavesAndLoadsTaskBoard()
        {
            // Arrange
            string sessionId = "session-task-1";
            var store = new AgentSessionStore(_tempWorkspace, sessionId);
            var board = new AgentTaskBoardRecord
            {
                SessionId = sessionId,
                Tasks = new List<AgentTaskRecord>
                {
                    new AgentTaskRecord { Id = "T1", Title = "Task 1", Status = "Running" }
                }
            };

            // Act
            await store.SaveTaskBoardAsync(board);
            var loadedBoard = await store.LoadTaskBoardAsync();

            // Assert
            Assert.NotNull(loadedBoard);
            Assert.Single(loadedBoard.Tasks);
            Assert.Equal("T1", loadedBoard.Tasks[0].Id);
            Assert.Equal("Running", loadedBoard.Tasks[0].Status);
        }

        [Fact]
        public async Task AgentSessionStore_AppendsProgressJsonl()
        {
            // Arrange
            string sessionId = "session-progress-1";
            var store = new AgentSessionStore(_tempWorkspace, sessionId);
            var evt1 = new AgentProgressEvent { AgentId = "agent-1", Type = "Info", Message = "Start" };
            var evt2 = new AgentProgressEvent { AgentId = "agent-1", Type = "ToolCall", Message = "ls" };

            // Act
            await store.AppendProgressAsync("agent-1", evt1);
            await store.AppendProgressAsync("agent-1", evt2);

            // Assert
            string progressFile = Path.Combine(store.SessionDir, "progress-agent-1.jsonl");
            Assert.True(File.Exists(progressFile));

            string[] lines = await File.ReadAllLinesAsync(progressFile);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"Type\":\"Info\"", lines[0]);
            Assert.Contains("\"Type\":\"ToolCall\"", lines[1]);
        }

        [Fact]
        public async Task AgentSessionStore_SavesResultMarkdown()
        {
            // Arrange
            string sessionId = "session-result-1";
            var store = new AgentSessionStore(_tempWorkspace, sessionId);
            string markdown = "# Result\nDone.";

            // Act
            await store.SaveResultAsync("agent-1", markdown);

            // Assert
            string resultFile = Path.Combine(store.SessionDir, "result-agent-1.md");
            Assert.True(File.Exists(resultFile));
            string content = await File.ReadAllTextAsync(resultFile);
            Assert.Equal(markdown, content);
        }

        [Fact]
        public void AgentSessionStore_ThrowsOnEmptyWorkspace()
        {
            Assert.Throws<ArgumentException>(() => new AgentSessionStore("", "id"));
        }

        [Theory]
        [InlineData("..\\escape")]
        [InlineData("../escape")]
        [InlineData("C:\\evil")]
        [InlineData("/evil")]
        [InlineData("agent:evil")]
        public void AgentSessionStore_ThrowsOnInvalidSessionId(string invalidSessionId)
        {
            Assert.Throws<ArgumentException>(() => new AgentSessionStore(_tempWorkspace, invalidSessionId));
        }

        [Theory]
        [InlineData("..\\escape")]
        [InlineData("../escape")]
        [InlineData("C:\\evil")]
        [InlineData("/evil")]
        [InlineData("agent:evil")]
        public async Task AgentSessionStore_LoadSessionRecordAsync_ReturnsNullOnInvalidSessionId(string invalidSessionId)
        {
            var result = await AgentSessionStore.LoadSessionRecordAsync(_tempWorkspace, invalidSessionId);
            Assert.Null(result);
        }

        [Theory]
        [InlineData("agent/evil")]
        [InlineData("agent\\evil")]
        [InlineData("..\\evil")]
        [InlineData("../evil")]
        [InlineData("C:\\evil")]
        public async Task AgentSessionStore_AppendProgressAsync_ThrowsOnInvalidAgentName(string invalidAgentName)
        {
            var store = new AgentSessionStore(_tempWorkspace, "session-test");
            var evt = new AgentProgressEvent { AgentId = "test", Type = "Info", Message = "Start" };

            await Assert.ThrowsAsync<ArgumentException>(() => store.AppendProgressAsync(invalidAgentName, evt));
        }

        [Theory]
        [InlineData("agent/evil")]
        [InlineData("agent\\evil")]
        [InlineData("..\\evil")]
        [InlineData("../evil")]
        [InlineData("C:\\evil")]
        public async Task AgentSessionStore_SaveResultAsync_ThrowsOnInvalidAgentName(string invalidAgentName)
        {
            var store = new AgentSessionStore(_tempWorkspace, "session-test");

            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveResultAsync(invalidAgentName, "content"));
        }
    }
}
