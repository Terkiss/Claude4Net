using Xunit;
using Moq;
using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;
using Claude4Net.Discord;
using System.Threading.Tasks;
using System;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class DiscordTests : IDisposable
    {
        private readonly string? _originalCwd;

        public DiscordTests()
        {
            _originalCwd = AppState.CurrentCwd;
            AppState.Tasks.Clear();
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.Tasks.Clear();
        }

        [Fact]
        public async Task DiscordOutputHandler_ShouldUpdateJobStatusToCompleted()
        {
            // Arrange
            var mockChannel = new Mock<ISocketMessageChannel>();
            string jobId = "test-job-1";
            var job = new DiscordJob
            {
                Id = jobId,
                Status = "Pending",
                DiscordStatus = DiscordJobStatus.Pending
            };
            AppState.Tasks[jobId] = job;

            var handler = new DiscordOutputHandler(mockChannel.Object, jobId);

            // Act
            await handler.WriteAsync("Hello World");

            // Assert
            Assert.Equal("Completed", job.Status);
            Assert.Equal(DiscordJobStatus.Completed, job.DiscordStatus);
            Assert.Equal("Hello World", job.ResponseMessage);
            mockChannel.Verify(m => m.SendMessageAsync("Hello World", false, null, null, null, null, null, null, null, MessageFlags.None), Times.Once);
        }

        [Fact]
        public async Task DiscordOutputHandler_ShouldSegmentLongMessages()
        {
            // Arrange
            var mockChannel = new Mock<ISocketMessageChannel>();
            string jobId = "test-job-2";
            var job = new DiscordJob { Id = jobId };
            AppState.Tasks[jobId] = job;

            var handler = new DiscordOutputHandler(mockChannel.Object, jobId);
            
            // Create a string longer than 2000 chars
            string longMessage = new string('A', 3000);

            // Act
            await handler.WriteAsync(longMessage);

            // Assert
            // 3000 chars should be split into 2 messages (1950 + 1050)
            mockChannel.Verify(m => m.SendMessageAsync(It.IsAny<string>(), false, null, null, null, null, null, null, null, MessageFlags.None), Times.Exactly(2));
        }

        [Fact]
        public void DiscordJob_ShouldInitializeWithCorrectType()
        {
            // Act
            var job = new DiscordJob();

            // Assert
            Assert.Equal("DiscordJob", job.Type);
            Assert.Equal("Pending", job.Status);
            Assert.Equal(DiscordJobStatus.Pending, job.DiscordStatus);
        }
    }
}
