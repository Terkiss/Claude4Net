using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Xunit;
using Claude4Net.Dashboard.Controllers;
using Claude4Net.Runtime.Jobs;
using System.Diagnostics;

namespace Claude4Net.Tests
{
    public class K103JobLongPollingTests
    {
        [Fact]
        public async Task GetFrame_ShouldWaitAndReturnNoContent_WhenSequenceDoesNotChange()
        {
            // Arrange
            JobStateTracker.Clear();
            string jobId = "test-job-1";
            JobStateTracker.GetOrCreateState(jobId);
            
            var controller = new JobController();
            var cts = new CancellationTokenSource(200); // 200ms timeout for test to be fast
            
            var stopwatch = Stopwatch.StartNew();

            // Act
            var result = await controller.GetFrame(jobId, afterSeq: 1, cts.Token);
            stopwatch.Stop();

            // Assert
            Assert.IsType<NoContentResult>(result);
            Assert.True(stopwatch.ElapsedMilliseconds >= 100, "Should have waited instead of returning immediately");
        }

        [Fact]
        public async Task GetFrame_ShouldReturnOk_WhenSequenceChanges()
        {
            // Arrange
            JobStateTracker.Clear();
            string jobId = "test-job-2";
            JobStateTracker.GetOrCreateState(jobId);
            
            var controller = new JobController();
            var cts = new CancellationTokenSource(2000);
            
            // Act
            // Start the long poll in background
            var task = controller.GetFrame(jobId, afterSeq: 1, cts.Token);
            
            // Update sequence from another thread
            await Task.Delay(100);
            JobStateTracker.UpdateState(jobId, s => { s.Progress = 50; });
            
            var result = await task;

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            dynamic value = okResult.Value;
            Assert.Equal(2, (int)value.GetType().GetProperty("sequence").GetValue(value, null));
            Assert.Equal(50, (double)value.GetType().GetProperty("progress").GetValue(value, null));
        }
    }
}
