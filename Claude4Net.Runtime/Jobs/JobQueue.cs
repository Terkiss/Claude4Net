using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.Runtime.Jobs
{
    public class JobQueue
    {
        private readonly ConcurrentQueue<JobRequest> _queue = new ConcurrentQueue<JobRequest>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _workerTask;

        public event Action<JobResult>? JobCompleted;

        public void Start()
        {
            _workerTask = Task.Run(ProcessQueueAsync);
        }

        public void Stop()
        {
            _cts.Cancel();
            _signal.Release();
        }

        public void Enqueue(JobRequest request)
        {
            _queue.Enqueue(request);
            _signal.Release();
        }

        private async Task ProcessQueueAsync()
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(_cts.Token);
                    if (_cts.Token.IsCancellationRequested) break;

                    if (_queue.TryDequeue(out var request))
                    {
                        var result = await JobWorker.RunJobAsync(request);
                        JobCompleted?.Invoke(result);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing job: {ex.Message}");
                }
            }
        }
    }
}
