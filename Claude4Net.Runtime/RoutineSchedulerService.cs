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
        private readonly ConcurrentDictionary<string, byte> _runningRoutines = new();
        private readonly ConcurrentDictionary<string, Task> _activeTasks = new();

        public TimeSpan MinimumIntervalFloor { get; set; } = TimeSpan.FromSeconds(5);

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
                        var routines = _store.ListRoutines().ToList();
                        foreach (var routine in routines)
                        {
                            if (_cts.Token.IsCancellationRequested) break;

                            if (!routine.Enabled)
                            {
                                if (routine.NextRun.HasValue)
                                {
                                    routine.NextRun = null;
                                    await _store.SaveAsync(routine);
                                }
                                continue;
                            }

                            if (routine.Trigger.Kind == RoutineTriggerKind.Webhook || routine.Trigger.Kind == RoutineTriggerKind.Event)
                            {
                                Console.WriteLine($"[Scheduler] Warning: Routine '{routine.Id}' has unsupported Webhook/Event trigger. Rejecting.");
                                if (routine.NextRun.HasValue)
                                {
                                    routine.NextRun = null;
                                    await _store.SaveAsync(routine);
                                }
                                continue;
                            }

                            if (routine.Trigger.Kind == RoutineTriggerKind.Manual)
                            {
                                if (routine.NextRun.HasValue)
                                {
                                    routine.NextRun = null;
                                    await _store.SaveAsync(routine);
                                }
                                continue;
                            }

                            // Calculate and update NextRun if needed
                            if (!routine.NextRun.HasValue)
                            {
                                var expectedNextRun = CalculateNextRun(routine, DateTimeOffset.UtcNow);
                                if (expectedNextRun.HasValue)
                                {
                                    routine.NextRun = expectedNextRun;
                                    await _store.SaveAsync(routine);
                                }
                            }

                            // Run if due and not already running
                            if (routine.NextRun.HasValue && DateTimeOffset.UtcNow >= routine.NextRun.Value)
                            {
                                if (!_runningRoutines.ContainsKey(routine.Id))
                                {
                                    // Start routine task
                                    var routineId = routine.Id;
                                    var task = Task.Run(async () =>
                                    {
                                        try
                                        {
                                            await RunRoutineWithTimeoutAndConcurrencyLimitAsync(routine);
                                        }
                                        finally
                                        {
                                            _activeTasks.TryRemove(routineId, out _);
                                        }
                                    });
                                    _activeTasks[routineId] = task;
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

        public async Task TriggerManualAsync(string routineId)
        {
            var routine = await _store.LoadAsync(routineId);
            if (routine == null) throw new InvalidOperationException($"Routine {routineId} not found.");
            if (!routine.Enabled) throw new InvalidOperationException($"Routine {routineId} is disabled.");

            if (routine.Trigger.Kind == RoutineTriggerKind.Webhook || routine.Trigger.Kind == RoutineTriggerKind.Event)
            {
                throw new InvalidOperationException($"Routine '{routineId}' has unsupported Webhook/Event trigger. Cannot trigger manually.");
            }

            if (_runningRoutines.ContainsKey(routine.Id))
            {
                throw new InvalidOperationException($"Routine {routineId} is already running.");
            }

            await RunRoutineWithTimeoutAndConcurrencyLimitAsync(routine);
        }

        private async Task RunRoutineWithTimeoutAndConcurrencyLimitAsync(RoutineDefinition routine)
        {
            if (!_runningRoutines.TryAdd(routine.Id, 0))
            {
                return;
            }

            try
            {
                using var timeoutCts = routine.Timeout.HasValue
                    ? new CancellationTokenSource(routine.Timeout.Value)
                    : new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

                await _runner.RunAsync(routine.Id, _workspaceRoot, routine.RequiredPermissionMode, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[Scheduler] Routine '{routine.Id}' execution timed out or cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Scheduler] Routine '{routine.Id}' execution error: {ex.Message}");
            }
            finally
            {
                _runningRoutines.TryRemove(routine.Id, out _);

                try
                {
                    var updated = await _store.LoadAsync(routine.Id);
                    if (updated != null)
                    {
                        updated.NextRun = CalculateNextRun(updated, DateTimeOffset.UtcNow);
                        await _store.SaveAsync(updated);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scheduler] Error updating NextRun after execution for routine '{routine.Id}': {ex.Message}");
                }
            }
        }

        public DateTimeOffset? CalculateNextRun(RoutineDefinition routine, DateTimeOffset baseTime)
        {
            if (!routine.Enabled) return null;
            if (routine.Trigger == null) return null;

            switch (routine.Trigger.Kind)
            {
                case RoutineTriggerKind.Interval:
                    if (TimeSpan.TryParse(routine.Trigger.Expression, out var interval))
                    {
                        if (interval < MinimumIntervalFloor)
                        {
                            interval = MinimumIntervalFloor;
                        }
                        return (routine.LastRun ?? baseTime) + interval;
                    }
                    return null;

                case RoutineTriggerKind.DailyTime:
                    if (TimeSpan.TryParse(routine.Trigger.Expression, out var dailyTime))
                    {
                        return CalculateNextDailyTime(dailyTime, routine.LastRun ?? baseTime);
                    }
                    return null;

                case RoutineTriggerKind.Manual:
                default:
                    return null;
            }
        }

        private static DateTimeOffset CalculateNextDailyTime(TimeSpan dailyTime, DateTimeOffset referenceTime)
        {
            var target = new DateTimeOffset(referenceTime.Date.Ticks + dailyTime.Ticks, referenceTime.Offset);
            if (target <= referenceTime)
            {
                target = target.AddDays(1);
            }
            return target;
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

            var activeTasks = _activeTasks.Values.ToList();
            if (activeTasks.Count > 0)
            {
                try
                {
                    await Task.WhenAll(activeTasks);
                }
                catch (Exception) { }
            }

            _cts.Dispose();
        }
    }
}
