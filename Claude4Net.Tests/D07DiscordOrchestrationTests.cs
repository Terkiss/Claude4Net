using Xunit;
using Claude4Net.SDK;
using System;

namespace Claude4Net.Tests
{
    public class D07DiscordOrchestrationTests
    {
        [Fact]
        public void DiscordJob_ShouldTrackStateTransitions()
        {
            var job = new DiscordJob { Id = "test-1" };
            Assert.Equal(DiscordJobStatus.Pending, job.DiscordStatus);
            Assert.Equal("Pending", job.Status);

            job.StartedAt = DateTime.UtcNow;
            job.DiscordStatus = DiscordJobStatus.Running;
            job.Status = "Running";

            Assert.Equal(DiscordJobStatus.Running, job.DiscordStatus);
            Assert.NotNull(job.StartedAt);

            job.CompletedAt = DateTime.UtcNow;
            job.DiscordStatus = DiscordJobStatus.Completed;
            job.Status = "Completed";

            Assert.Equal(DiscordJobStatus.Completed, job.DiscordStatus);
            Assert.True(job.Duration > TimeSpan.Zero);
        }

        [Fact]
        public void DiscordResponseFormatter_ShouldFormatStartCorrectly()
        {
            string res = DiscordResponseFormatter.FormatStart("user123", "Hello bot, do something complex for me please.");
            Assert.Contains("🚀 **Task Started**", res);
            Assert.Contains("@user123", res);
            Assert.Contains("Hello bot", res);
        }

        [Fact]
        public void DiscordResponseFormatter_ShouldFormatSuccessWithDuration()
        {
            string res = DiscordResponseFormatter.FormatSuccess("Result text", TimeSpan.FromSeconds(5.5));
            Assert.Contains("✅ **Task Completed**", res);
            Assert.Contains("Result text", res);
            Assert.Contains("5.5s", res);
        }

        [Fact]
        public void DiscordResponseFormatter_ShouldTruncateLongText()
        {
            string longText = new string('A', 2000);
            string res = DiscordResponseFormatter.FormatSuccess(longText);
            Assert.True(res.Length < 2000); // Should be truncated around 1500
            Assert.Contains("...", res);
        }
    }
}
