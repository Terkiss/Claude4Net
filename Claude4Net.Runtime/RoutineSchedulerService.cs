using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class RoutineSchedulerService : IAsyncDisposable
    {
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;
        private readonly string _workspaceRoot;
        private readonly CancellationTokenSource _cts;
        private Task? _backgroundTask;

        public RoutineSchedulerService(RoutineStore store, RoutineRunner runner, string workspaceRoot)
        {
            _store = store;
            _runner = runner;
            _workspaceRoot = workspaceRoot;
            _cts = new CancellationTokenSource();
        }

        public void Start()
        {
            _backgroundTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var routines = _store.ListRoutines().Where(r => r.Enabled).ToList();
                        foreach (var routine in routines)
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            if (routine.Trigger.Kind == RoutineTriggerKind.Interval && TimeSpan.TryParse(routine.Trigger.Expression, out var interval))
                            {
                                var records = _store.GetRunRecords(routine.Id).OrderByDescending(r => r.StartedAt).ToList();
                                var lastRun = records.FirstOrDefault();

                                if (lastRun == null || (DateTimeOffset.UtcNow - lastRun.StartedAt) >= interval)
                                {
                                    try
                                    {
                                        await _runner.RunAsync(routine.Id, _workspaceRoot, routine.RequiredPermissionMode);
                                    }
                                    catch (Exception ex)
                                    {
                                        // Failure logging
                                        Console.WriteLine($"[Scheduler] Error running routine {routine.Id}: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Scheduler] General error: {ex.Message}");
                    }

                    try
                    {
                        await Task.Delay(100, _cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _cts.Token);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            if (_backgroundTask != null)
            {
                try
                {
                    await _backgroundTask;
                }
                catch (OperationCanceledException) { }
            }
            _cts.Dispose();
        }
    }
}
