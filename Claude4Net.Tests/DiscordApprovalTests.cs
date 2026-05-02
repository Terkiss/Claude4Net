using Xunit;
using Moq;
using Discord;
using Discord.WebSocket;
using Claude4Net.SDK;
using Claude4Net.Discord;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class DiscordApprovalTests : IDisposable
    {
        public DiscordApprovalTests()
        {
            AppState.Tasks.Clear();
            AppState.DiscordAllowedApproverIds.Clear();
        }

        public void Dispose()
        {
            AppState.Tasks.Clear();
            AppState.DiscordAllowedApproverIds.Clear();
        }

        [Fact]
        public async Task DiscordJob_StatusTransitions_Correctly()
        {
            // Arrange
            var jobId = "test-job-approval";
            var job = new DiscordJob { Id = jobId };
            AppState.Tasks[jobId] = job;

            // Act & Assert (Initial)
            Assert.Equal(DiscordJobStatus.Pending, job.DiscordStatus);

            // Transition to WaitingApproval
            job.DiscordStatus = DiscordJobStatus.WaitingApproval;
            Assert.Equal(DiscordJobStatus.WaitingApproval, job.DiscordStatus);

            // Transition to Running
            job.DiscordStatus = DiscordJobStatus.Running;
            Assert.Equal(DiscordJobStatus.Running, job.DiscordStatus);

            // Transition to Completed
            job.DiscordStatus = DiscordJobStatus.Completed;
            Assert.Equal(DiscordJobStatus.Completed, job.DiscordStatus);
        }

        [Fact]
        public void AppState_DiscordAllowedApproverIds_Works()
        {
            // Act
            AppState.DiscordAllowedApproverIds.Add(12345ul);
            
            // Assert
            Assert.Contains(12345ul, AppState.DiscordAllowedApproverIds);
            Assert.DoesNotContain(67890ul, AppState.DiscordAllowedApproverIds);
        }

        [Fact]
        public void DiscordJob_CompleteAsync_Protection_Works()
        {
            // Arrange
            var job = new DiscordJob { Id = "test-protection", DiscordStatus = DiscordJobStatus.Denied };
            AppState.Tasks[job.Id] = job;
            var handler = new DiscordOutputHandler(null!, job.Id);

            // Act
            // CompleteAsync should not change status if it's already Denied
            var task = handler.CompleteAsync("Some message");
            task.Wait();

            // Assert
            Assert.Equal(DiscordJobStatus.Denied, job.DiscordStatus);
            Assert.NotEqual("Completed", job.Status);
        }

        [Fact]
        public async Task DiscordRetryUtils_RetriesOnFailure()
        {
            // Arrange
            int callCount = 0;
            Func<Task<bool>> action = async () => 
            {
                callCount++;
                if (callCount < 3) throw new Exception("Transient error");
                return await Task.FromResult(true);
            };

            // Act
            bool result = await DiscordRetryUtils.ExecuteWithRetryAsync(action, maxRetries: 3);

            // Assert
            Assert.True(result);
            Assert.Equal(3, callCount);
        }

        [Fact]
        public async Task DiscordRetryUtils_ThrowsAfterMaxRetries()
        {
            // Arrange
            int callCount = 0;
            Func<Task<bool>> action = async () => 
            {
                callCount++;
                throw new Exception("Permanent error");
            };

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => DiscordRetryUtils.ExecuteWithRetryAsync(action, maxRetries: 2));
            Assert.Equal(3, callCount); // Initial call + 2 retries
        }
    }
}
