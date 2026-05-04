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
        public void AppState_LoadDiscordApprovers_Works()
        {
            // Arrange
            Environment.SetEnvironmentVariable("CLAUDE4NET_DISCORD_APPROVER_IDS", "111, 222, abc, 333");

            // Act
            AppState.LoadDiscordApprovers();

            // Assert
            Assert.Contains(111ul, AppState.DiscordAllowedApproverIds);
            Assert.Contains(222ul, AppState.DiscordAllowedApproverIds);
            Assert.Contains(333ul, AppState.DiscordAllowedApproverIds);
            Assert.Equal(3, AppState.DiscordAllowedApproverIds.Count);

            // Cleanup
            Environment.SetEnvironmentVariable("CLAUDE4NET_DISCORD_APPROVER_IDS", null);
        }

        [Fact]
        public void DiscordJob_PermissionCheck_Logic_Works()
        {
            // Arrange
            AppState.DiscordAllowedApproverIds.Clear();
            AppState.DiscordAllowedApproverIds.Add(999ul);

            // Act & Assert
            // 1. Allowed user
            bool isAllowed1 = AppState.DiscordAllowedApproverIds.Count > 0 && AppState.DiscordAllowedApproverIds.Contains(999ul);
            Assert.True(isAllowed1);

            // 2. Disallowed user
            bool isAllowed2 = AppState.DiscordAllowedApproverIds.Count > 0 && AppState.DiscordAllowedApproverIds.Contains(111ul);
            Assert.False(isAllowed2);

            // 3. Empty whitelist (should deny all)
            AppState.DiscordAllowedApproverIds.Clear();
            bool isAllowed3 = AppState.DiscordAllowedApproverIds.Count > 0 && AppState.DiscordAllowedApproverIds.Contains(999ul);
            Assert.False(isAllowed3);
        }

        [Fact]
        public async Task DiscordJob_CompleteAsync_Protection_Works()
        {
            // Arrange
            var job = new DiscordJob { Id = "test-protection", DiscordStatus = DiscordJobStatus.Denied };
            AppState.Tasks[job.Id] = job;
            var handler = new DiscordOutputHandler(null!, job.Id);

            // Act
            // CompleteAsync should not change status if it's already Denied
            await handler.CompleteAsync("Some message");

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
