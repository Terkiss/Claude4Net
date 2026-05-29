using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Jobs;
using Claude4Net.SDK;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    [Trait("Category", "K101")]
    public class K101Tests : IAsyncLifetime
    {
        public async Task InitializeAsync()
        {
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        public async Task DisposeAsync()
        {
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
        }

        [Fact]
        public async Task SequentialRunOrdering_QueueProcessesInFifoOrder()
        {
            // Arrange
            var queue = new JobQueue();
            var results = new System.Collections.Generic.List<JobResult>();
            var tcs1 = new TaskCompletionSource<JobResult>();
            var tcs2 = new TaskCompletionSource<JobResult>();

            queue.JobCompleted += (res) =>
            {
                lock (results)
                {
                    results.Add(res);
                    if (results.Count == 1) tcs1.SetResult(res);
                    else if (results.Count == 2) tcs2.SetResult(res);
                }
            };

            queue.Start();

            var req1 = new JobRequest { JobId = "job-001", RepoUrl = "https://example.com/repo1.git", BaseBranch = "main" };
            var req2 = new JobRequest { JobId = "job-002", RepoUrl = "https://example.com/repo2.git", BaseBranch = "main" };

            // Act
            queue.Enqueue(req1);
            queue.Enqueue(req2);

            await Task.WhenAll(tcs1.Task, tcs2.Task);
            queue.Stop();

            // Assert
            Assert.Equal(2, results.Count);
            Assert.Equal("job-001", results[0].JobId);
            Assert.Equal("job-002", results[1].JobId);
        }

        [Fact]
        public void PathTraversalBlock_ThrowsSecurityException_OnTraversalJobId()
        {
            // Act & Assert
            Assert.Throws<SecurityException>(() =>
                GitWorkspaceManager.PrepareWorkspace("../invalid-job", "https://example.com/repo.git", "main")
            );

            Assert.Throws<SecurityException>(() =>
                GitWorkspaceManager.PrepareWorkspace(@"..\invalid-job", "https://example.com/repo.git", "main")
            );

            Assert.Throws<SecurityException>(() =>
                GitWorkspaceManager.PrepareWorkspace("job/dir", "https://example.com/repo.git", "main")
            );
        }
    }
}
