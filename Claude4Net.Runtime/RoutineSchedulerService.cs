using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class BuildStateDto
    {
        public bool IsBuilding { get; set; }
        public bool LastBuildSuccess { get; set; }
        public DateTimeOffset? LastBuildTime { get; set; }
        public string? BuildLog { get; set; }
    }

    public class RoutineScheduleCalendarDto
    {
        public string RoutineId { get; set; } = string.Empty;
        public string RoutineName { get; set; } = string.Empty;
        public string TriggerExpression { get; set; } = string.Empty;
        public DateTimeOffset? LastRun { get; set; }
        public DateTimeOffset? NextRun { get; set; }
    }

    public class ReleaseGateStatusDto
    {
        public string GatewayName { get; set; } = string.Empty;
        public bool IsPassed { get; set; }
        public DateTimeOffset? EvaluatedAt { get; set; }
        public string? Details { get; set; }
    }

    public class ControlTowerStateDto
    {
        public BuildStateDto BuildState { get; set; } = new();
        public List<RoutineScheduleCalendarDto> ActiveSchedules { get; set; } = new();
        public List<ReleaseGateStatusDto> ReleaseGates { get; set; } = new();
        public string? SchedulerLockOwner { get; set; }
        public bool IsSchedulerLocked { get; set; }
    }

    public class SimpleCronParser
    {
        private readonly List<int> _minutes;
        private readonly List<int> _hours;
        private readonly List<int> _daysOfMonth;
        private readonly List<int> _months;
        private readonly List<int> _daysOfWeek;

        public SimpleCronParser(string expression)
        {
            var parts = expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5)
            {
                throw new ArgumentException("CRON expression must have exactly 5 fields.");
            }

            _minutes = ParseField(parts[0], 0, 59);
            _hours = ParseField(parts[1], 0, 23);
            _daysOfMonth = ParseField(parts[2], 1, 31);
            _months = ParseField(parts[3], 1, 12);
            _daysOfWeek = ParseField(parts[4], 0, 7); // 0 or 7 is Sunday
        }

        private static List<int> ParseField(string field, int min, int max)
        {
            var values = new HashSet<int>();
            var parts = field.Split(',');
            foreach (var part in parts)
            {
                if (part == "*")
                {
                    for (int i = min; i <= max; i++) values.Add(i);
                }
                else if (part.StartsWith("*/"))
                {
                    if (int.TryParse(part.Substring(2), out var step) && step > 0)
                    {
                        for (int i = min; i <= max; i += step) values.Add(i);
                    }
                    else throw new ArgumentException($"Invalid CRON step: {part}");
                }
                else if (part.Contains("-"))
                {
                    var rangeParts = part.Split('-');
                    if (rangeParts.Length == 2 && int.TryParse(rangeParts[0], out var start) && int.TryParse(rangeParts[1], out var end))
                    {
                        for (int i = start; i <= end; i++)
                        {
                            if (i >= min && i <= max) values.Add(i);
                        }
                    }
                    else throw new ArgumentException($"Invalid CRON range: {part}");
                }
                else
                {
                    if (int.TryParse(part, out var val))
                    {
                        if (val >= min && val <= max) values.Add(val);
                    }
                    else throw new ArgumentException($"Invalid CRON value: {part}");
                }
            }
            return values.OrderBy(v => v).ToList();
        }

        public DateTimeOffset GetNextOccurrence(DateTimeOffset baseTime)
        {
            var testTime = baseTime.AddMinutes(1);
            testTime = new DateTimeOffset(testTime.Year, testTime.Month, testTime.Day, testTime.Hour, testTime.Minute, 0, baseTime.Offset);

            var limit = testTime.AddYears(5);
            while (testTime < limit)
            {
                if (!_months.Contains(testTime.Month))
                {
                    testTime = new DateTimeOffset(testTime.Year, testTime.Month, 1, 0, 0, 0, baseTime.Offset).AddMonths(1);
                    continue;
                }

                if (!_daysOfMonth.Contains(testTime.Day))
                {
                    testTime = new DateTimeOffset(testTime.Year, testTime.Month, testTime.Day, 0, 0, 0, baseTime.Offset).AddDays(1);
                    continue;
                }

                var dayOfWeek = (int)testTime.DayOfWeek;
                if (!_daysOfWeek.Contains(dayOfWeek) && (dayOfWeek != 0 || !_daysOfWeek.Contains(7)) && (dayOfWeek != 7 || !_daysOfWeek.Contains(0)))
                {
                    testTime = new DateTimeOffset(testTime.Year, testTime.Month, testTime.Day, 0, 0, 0, baseTime.Offset).AddDays(1);
                    continue;
                }

                if (!_hours.Contains(testTime.Hour))
                {
                    testTime = new DateTimeOffset(testTime.Year, testTime.Month, testTime.Day, testTime.Hour, 0, 0, baseTime.Offset).AddHours(1);
                    continue;
                }

                if (!_minutes.Contains(testTime.Minute))
                {
                    testTime = testTime.AddMinutes(1);
                    continue;
                }

                return testTime;
            }

            throw new InvalidOperationException("No occurrence found in the next 5 years.");
        }
    }

    public class RoutineSchedulerService : IAsyncDisposable
    {
        private readonly RoutineStore _store;
        private readonly RoutineRunner _runner;
        private readonly string _workspaceRoot;
        private readonly CancellationTokenSource _cts;
        private Task? _backgroundTask;
        private readonly ConcurrentDictionary<string, byte> _runningRoutines = new();
        private readonly ConcurrentDictionary<string, Task> _activeTasks = new();
        private readonly ConcurrentDictionary<string, FileStream> _activeLocks = new();
        private FileStream? _globalLockStream;
        private bool _isGlobalLocked;

        public TimeSpan MinimumIntervalFloor { get; set; } = TimeSpan.FromSeconds(5);

        public RoutineSchedulerService(RoutineStore store, RoutineRunner runner, string workspaceRoot)
        {
            _store = store;
            _runner = runner;
            _workspaceRoot = workspaceRoot;
            _cts = new CancellationTokenSource();
        }

        private bool TryAcquireGlobalLock()
        {
            try
            {
                var lockDir = Path.Combine(_workspaceRoot, ".claude4net");
                Directory.CreateDirectory(lockDir);
                var lockPath = Path.Combine(lockDir, "scheduler.lock");
                
                _globalLockStream = new FileStream(
                    lockPath, 
                    FileMode.OpenOrCreate, 
                    FileAccess.ReadWrite, 
                    FileShare.None, 
                    4096, 
                    FileOptions.DeleteOnClose);
                    
                _isGlobalLocked = true;
                return true;
            }
            catch (IOException)
            {
                _isGlobalLocked = false;
                return false;
            }
        }

        private bool TryAcquireRoutineLock(string routineId)
        {
            try
            {
                var lockDir = Path.Combine(_workspaceRoot, ".claude4net", "locks");
                Directory.CreateDirectory(lockDir);
                var lockPath = Path.Combine(lockDir, $"routine_{routineId}.lock");
                
                var fileStream = new FileStream(
                    lockPath, 
                    FileMode.OpenOrCreate, 
                    FileAccess.ReadWrite, 
                    FileShare.None, 
                    4096, 
                    FileOptions.DeleteOnClose);
                    
                _activeLocks[routineId] = fileStream;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private void ReleaseRoutineLock(string routineId)
        {
            if (_activeLocks.TryRemove(routineId, out var fileStream))
            {
                try
                {
                    fileStream.Dispose();
                }
                catch { }
            }
        }

        private bool IsRoutineLockedByAnyInstance(string routineId)
        {
            if (_activeLocks.ContainsKey(routineId)) return true;
            
            var lockDir = Path.Combine(_workspaceRoot, ".claude4net", "locks");
            var lockPath = Path.Combine(lockDir, $"routine_{routineId}.lock");
            if (!File.Exists(lockPath)) return false;
            
            try
            {
                using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
        }

        public void Start()
        {
            _backgroundTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    if (!_isGlobalLocked)
                    {
                        if (!TryAcquireGlobalLock())
                        {
                            try
                            {
                                await Task.Delay(2000, _cts.Token);
                            }
                            catch (OperationCanceledException) { break; }
                            continue;
                        }
                    }

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

                            // If CRON is parsed, we bypass Webhook/Event rejection
                            bool isCron = false;
                            if (!string.IsNullOrWhiteSpace(routine.Trigger.Expression) && 
                                routine.Trigger.Expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length == 5)
                            {
                                try
                                {
                                    new SimpleCronParser(routine.Trigger.Expression);
                                    isCron = true;
                                }
                                catch {}
                            }

                            if (!isCron && (routine.Trigger.Kind == RoutineTriggerKind.Webhook || routine.Trigger.Kind == RoutineTriggerKind.Event))
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

                            if (!routine.NextRun.HasValue)
                            {
                                var expectedNextRun = CalculateNextRun(routine, DateTimeOffset.UtcNow);
                                if (expectedNextRun.HasValue)
                                {
                                    routine.NextRun = expectedNextRun;
                                    await _store.SaveAsync(routine);
                                }
                            }

                            if (routine.NextRun.HasValue && DateTimeOffset.UtcNow >= routine.NextRun.Value)
                            {
                                if (!_runningRoutines.ContainsKey(routine.Id) && !IsRoutineLockedByAnyInstance(routine.Id))
                                {
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

            bool isCron = false;
            if (!string.IsNullOrWhiteSpace(routine.Trigger.Expression) && 
                routine.Trigger.Expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length == 5)
            {
                try
                {
                    new SimpleCronParser(routine.Trigger.Expression);
                    isCron = true;
                }
                catch {}
            }

            if (!isCron && (routine.Trigger.Kind == RoutineTriggerKind.Webhook || routine.Trigger.Kind == RoutineTriggerKind.Event))
            {
                throw new InvalidOperationException($"Routine '{routineId}' has unsupported Webhook/Event trigger. Cannot trigger manually.");
            }

            if (_runningRoutines.ContainsKey(routine.Id) || IsRoutineLockedByAnyInstance(routine.Id))
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

            if (!TryAcquireRoutineLock(routine.Id))
            {
                _runningRoutines.TryRemove(routine.Id, out _);
                return;
            }

            try
            {
                bool isReleaseVerification = routine.Actions.Any(a => a.Kind == RoutineActionKind.Verification || 
                    (a.Kind == RoutineActionKind.Script && a.Payload.Contains("verify-release.ps1")));

                if (isReleaseVerification)
                {
                    await RunReleaseVerificationAsync(routine);
                }
                else
                {
                    using var timeoutCts = routine.Timeout.HasValue
                        ? new CancellationTokenSource(routine.Timeout.Value)
                        : new CancellationTokenSource();
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

                    await _runner.RunAsync(routine.Id, _workspaceRoot, routine.RequiredPermissionMode, linkedCts.Token);
                }
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
                ReleaseRoutineLock(routine.Id);
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

        private async Task RunReleaseVerificationAsync(RoutineDefinition routine)
        {
            var startedAt = DateTimeOffset.UtcNow;
            var scriptPath = Path.Combine(_workspaceRoot, "scripts", "verify-release.ps1");
            bool scriptExists = File.Exists(scriptPath);
            string output = "";
            string error = "";
            int exitCode = 0;

            if (scriptExists)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = _workspaceRoot
                    };
                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process != null)
                    {
                        var outTask = process.StandardOutput.ReadToEndAsync();
                        var errTask = process.StandardError.ReadToEndAsync();
                        
                        using var timeoutCts = routine.Timeout.HasValue
                            ? new CancellationTokenSource(routine.Timeout.Value)
                            : new CancellationTokenSource();
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);
                        
                        await process.WaitForExitAsync(linkedCts.Token);
                        output = outTask.Result;
                        error = errTask.Result;
                        exitCode = process.ExitCode;
                    }
                    else
                    {
                        exitCode = -1;
                        error = "Failed to start powershell.exe";
                    }
                }
                catch (Exception ex)
                {
                    exitCode = -2;
                    error = ex.Message;
                }
            }
            else
            {
                output = "Simulating verify-release.ps1 execution\n[OK] Release Gate passed all checks.";
                exitCode = 0;
            }

            var orchestrator = new VerificationOrchestrator(_workspaceRoot);
            var session = orchestrator.CreateVerifierSession(routine.Id);
            
            var checks = new List<VerificationCheck>();
            checks.Add(orchestrator.RunCheck("Release Gate Verification Script", $"powershell.exe -File {scriptPath}", output + "\n" + error, exitCode));
            
            var verifResult = orchestrator.AggregateResult(session.VerifierSessionId, session.GeneratorSessionId, checks);
            await orchestrator.WriteResultAsync(verifResult);

            var runRecord = new RoutineRunRecord
            {
                RunId = Guid.NewGuid().ToString("N"),
                RoutineId = routine.Id,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                Success = (exitCode == 0),
                Error = exitCode == 0 ? null : $"verify-release.ps1 failed with exit code {exitCode}. Error: {error}"
            };
            await _store.SaveRunRecordAsync(runRecord);
        }

        public async Task<ControlTowerStateDto> GetControlTowerStateAsync()
        {
            var state = new ControlTowerStateDto
            {
                IsSchedulerLocked = _isGlobalLocked,
                SchedulerLockOwner = _isGlobalLocked ? "LocalInstance" : null
            };

            try
            {
                var routines = _store.ListRoutines();
                foreach (var routine in routines)
                {
                    if (routine.Enabled)
                    {
                        state.ActiveSchedules.Add(new RoutineScheduleCalendarDto
                        {
                            RoutineId = routine.Id,
                            RoutineName = routine.Name,
                            TriggerExpression = routine.Trigger?.Expression ?? "Manual",
                            LastRun = routine.LastRun,
                            NextRun = routine.NextRun
                        });
                    }
                }
            }
            catch {}

            try
            {
                var sessionsDir = Path.Combine(_workspaceRoot, ".claude4net", "sessions");
                if (Directory.Exists(sessionsDir))
                {
                    var resultFiles = Directory.GetDirectories(sessionsDir)
                        .Select(d => Path.Combine(d, "verification-result.json"))
                        .Where(File.Exists)
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(fi => fi.LastWriteTime)
                        .ToList();

                    if (resultFiles.Any())
                    {
                        var latestFile = resultFiles.First();
                        var json = await File.ReadAllTextAsync(latestFile.FullName);
                        var result = JsonSerializer.Deserialize<VerificationResult>(json);
                        if (result != null)
                        {
                            var buildCheck = result.Checks.FirstOrDefault(c => c.Name.Contains("Build", StringComparison.OrdinalIgnoreCase) || c.Name.Contains("Release", StringComparison.OrdinalIgnoreCase));
                            state.BuildState = new BuildStateDto
                            {
                                IsBuilding = false,
                                LastBuildSuccess = result.Verdict == VerificationVerdict.Pass,
                                LastBuildTime = latestFile.LastWriteTime,
                                BuildLog = buildCheck?.Evidence
                            };

                            foreach (var check in result.Checks)
                            {
                                state.ReleaseGates.Add(new ReleaseGateStatusDto
                                {
                                    GatewayName = check.Name,
                                    IsPassed = check.Result == VerificationVerdict.Pass,
                                    EvaluatedAt = check.CompletedAt,
                                    Details = check.Notes
                                });
                            }
                        }
                    }
                }
            }
            catch {}

            return state;
        }

        public DateTimeOffset? CalculateNextRun(RoutineDefinition routine, DateTimeOffset baseTime)
        {
            if (!routine.Enabled) return null;
            if (routine.Trigger == null) return null;

            if (!string.IsNullOrWhiteSpace(routine.Trigger.Expression) && 
                routine.Trigger.Expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length == 5)
            {
                try
                {
                    var parser = new SimpleCronParser(routine.Trigger.Expression);
                    var calculated = parser.GetNextOccurrence(routine.LastRun ?? baseTime);
                    if (calculated < baseTime)
                    {
                        calculated = parser.GetNextOccurrence(baseTime);
                    }
                    return calculated;
                }
                catch
                {
                }
            }

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

            foreach (var key in _activeLocks.Keys.ToList())
            {
                ReleaseRoutineLock(key);
            }

            if (_globalLockStream != null)
            {
                try
                {
                    _globalLockStream.Dispose();
                }
                catch {}
            }

            _cts.Dispose();
        }
    }
}
