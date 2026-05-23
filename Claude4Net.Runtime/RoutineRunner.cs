using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class NullOutputHandler : IOutputHandler
    {
        public Task WriteAsync(string text) => Task.CompletedTask;
        public Task CompleteAsync(string finalMessage) => Task.CompletedTask;
        public Task SendFileAsync(string filePath, string? text = null) => Task.CompletedTask;
    }

    public class RoutineRunner
    {
        private readonly RoutineStore _store;
        private readonly PermissionEnforcer _permissionEnforcer;
        private readonly PathSafetyEvaluator _pathSafety;
        private readonly IServiceProvider? _serviceProvider;

        public RoutineRunner(RoutineStore store, PermissionEnforcer permissionEnforcer, PathSafetyEvaluator pathSafety, IServiceProvider? serviceProvider = null)
        {
            _store = store;
            _permissionEnforcer = permissionEnforcer;
            _pathSafety = pathSafety;
            _serviceProvider = serviceProvider;
        }

        public async Task<RoutineRunRecord> RunAsync(string routineId, string workspaceRoot, PermissionMode currentSessionMode)
        {
            var record = new RoutineRunRecord
            {
                RunId = Guid.NewGuid().ToString("N"),
                RoutineId = routineId,
                StartedAt = DateTimeOffset.UtcNow
            };

            string? oldCwd = AppState.CurrentCwd;
            HookContext? routineHookCtx = null;
            var hookPipeline = _serviceProvider?.GetService<HookPipeline>();

            try
            {
                var routine = await _store.LoadAsync(routineId);
                if (routine == null) throw new InvalidOperationException($"Routine {routineId} not found.");
                if (!routine.Enabled) throw new InvalidOperationException($"Routine {routineId} is disabled.");
                if (routine.Actions == null || routine.Actions.Count == 0)
                {
                    throw new InvalidOperationException("Routine must contain at least one action.");
                }

                string actualWorkspaceRoot = !string.IsNullOrWhiteSpace(routine.WorkspaceRoot) ? routine.WorkspaceRoot : workspaceRoot;
                if (string.IsNullOrWhiteSpace(actualWorkspaceRoot))
                {
                    actualWorkspaceRoot = oldCwd ?? Directory.GetCurrentDirectory();
                }
                AppState.CurrentCwd = actualWorkspaceRoot;

                routine.LastRun = record.StartedAt;
                await _store.SaveAsync(routine);

                if (PermissionEnforcer.Normalize(currentSessionMode) == PermissionMode.ReadOnly &&
                    PermissionEnforcer.Normalize(routine.RequiredPermissionMode) != PermissionMode.ReadOnly)
                {
                    throw new UnauthorizedAccessException("Current session is ReadOnly, but routine requires higher permissions.");
                }

                // 3. Before Hooks: Trigger hook pipeline before execution
                if (hookPipeline != null)
                {
                    routineHookCtx = new HookContext
                    {
                        ToolName = "Routine:" + routineId,
                        Arguments = routineId,
                        SessionId = AppState.SessionId
                    };
                    var beforeResult = await hookPipeline.ExecuteBeforeAsync(routineHookCtx);
                    if (beforeResult != null && beforeResult.ShouldAbort)
                    {
                        throw new OperationCanceledException($"Execution aborted by hook '{beforeResult.HookName}': {beforeResult.AbortReason}");
                    }
                }

                var allowedSlashCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "status", "help", "ls", "pwd", "checkpoint", "handoff", "verify", "spec", "routine", "coordinate"
                };

                foreach (var action in routine.Actions)
                {
                    if (action == null) continue;

                    string payload = action.Payload ?? string.Empty;

                    // 2. Path & Workspace checks
                    if (action.Kind == RoutineActionKind.Script)
                    {
                        string fullScriptPath = Path.IsPathRooted(payload)
                            ? Path.GetFullPath(payload)
                            : Path.GetFullPath(Path.Combine(actualWorkspaceRoot, payload));
                        if (!fullScriptPath.StartsWith(Path.GetFullPath(actualWorkspaceRoot), StringComparison.OrdinalIgnoreCase))
                        {
                            throw new UnauthorizedAccessException($"Script execution denied: Path '{payload}' is outside the workspace.");
                        }

                        var pathResult = _pathSafety.EvaluateSinglePathSafety(payload);
                        if (pathResult == PathSafetyResult.Outside)
                        {
                            throw new UnauthorizedAccessException($"Script execution denied: Path '{payload}' is outside the workspace.");
                        }

                        // 3. Permission evaluation
                        var eval = _permissionEnforcer.Evaluate(
                            currentSessionMode,
                            "bash",
                            pathResult,
                            true,
                            new CommandRiskAssessment(CommandRiskLevel.Dangerous, "script execution", Array.Empty<string>())
                        );
                        if (eval.Decision == PermissionDecision.Deny)
                        {
                            throw new UnauthorizedAccessException($"Script execution denied: {eval.Reason}");
                        }
                        if (PermissionEnforcer.Normalize(currentSessionMode) == PermissionMode.ReadOnly)
                        {
                            throw new UnauthorizedAccessException("ReadOnly mode blocks write and script actions.");
                        }
                    }
                    else if (action.Kind == RoutineActionKind.SlashCommand)
                    {
                        string fullPayload = payload.Trim();
                        string cmdName = fullPayload.StartsWith('/') || fullPayload.StartsWith('!')
                            ? fullPayload[1..].Split(' ')[0]
                            : fullPayload.Split(' ')[0];

                        if (!allowedSlashCommands.Contains(cmdName))
                        {
                            throw new UnauthorizedAccessException($"Command '{cmdName}' is not allowlisted for routine execution.");
                        }

                        // Block write/sensitive commands in ReadOnly mode
                        if (PermissionEnforcer.Normalize(currentSessionMode) == PermissionMode.ReadOnly)
                        {
                            var writeCmds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            {
                                "checkpoint", "handoff", "coordinate"
                            };
                            if (writeCmds.Contains(cmdName))
                            {
                                throw new UnauthorizedAccessException($"ReadOnly mode blocks modifying slash command '{cmdName}'.");
                            }
                        }
                    }

                    // 4. Preflight Checkpoint
                    bool isModifying = false;
                    if (action.Kind == RoutineActionKind.Script)
                    {
                        isModifying = true;
                    }
                    else if (action.Kind == RoutineActionKind.SlashCommand)
                    {
                        string fullPayload = payload.Trim();
                        string cmdName = fullPayload.StartsWith('/') || fullPayload.StartsWith('!')
                            ? fullPayload[1..].Split(' ')[0]
                            : fullPayload.Split(' ')[0];
                        var readonlyCmds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        {
                            "status", "help", "ls", "pwd", "verify"
                        };
                        if (!readonlyCmds.Contains(cmdName))
                        {
                            isModifying = true;
                        }
                    }

                    if (isModifying)
                    {
                        var checkpointStore = new CheckpointStore(actualWorkspaceRoot, AppState.SessionId ?? Guid.NewGuid().ToString("N"));
                        var targetFiles = GetWorkspaceFilesToBackup(actualWorkspaceRoot);
                        string checkpointId = await checkpointStore.CreateCheckpointAsync(
                            toolCallId: $"routine-action-{action.Kind}-{Guid.NewGuid():N}"[..40],
                            toolName: action.Kind.ToString(),
                            files: targetFiles,
                            description: $"Preflight checkpoint for routine {routineId} action {action.Kind}",
                            includeMemoryState: true
                        );
                    }

                    // 6. Execute Action
                    if (action.Kind == RoutineActionKind.Script)
                    {
                        var (output, error, exitCode) = await RunProcessAsync(payload, actualWorkspaceRoot);
                        if (exitCode != 0)
                        {
                            throw new InvalidOperationException($"Script execution failed with exit code {exitCode}. Error: {error}");
                        }
                    }
                    else if (action.Kind == RoutineActionKind.SlashCommand)
                    {
                        string fullPayload = payload.Trim();
                        string cmdName = fullPayload.StartsWith('/') || fullPayload.StartsWith('!')
                            ? fullPayload[1..].Split(' ')[0]
                            : fullPayload.Split(' ')[0];

                        string arguments = "";
                        int spaceIdx = fullPayload.IndexOf(' ');
                        if (spaceIdx > 0)
                        {
                            arguments = fullPayload[(spaceIdx + 1)..];
                        }
                        var sp = _serviceProvider ?? new ServiceCollection().BuildServiceProvider();
                        await ExecuteSlashCommandReflectionAsync(cmdName, arguments, sp);
                    }
                    else if (action.Kind == RoutineActionKind.Prompt)
                    {
                        var broker = _serviceProvider?.GetService<IInputBroker>();
                        if (broker == null)
                        {
                            throw new InvalidOperationException("Input broker is not available to handle prompt run request.");
                        }
                        var output = _serviceProvider?.GetService<IOutputHandler>() ?? new NullOutputHandler();
                        var approval = _serviceProvider?.GetService<IUserApprovalHandler>();
                        broker.TryWrite(new InputContext(payload, output, approval));
                    }
                    else if (action.Kind == RoutineActionKind.Verification)
                    {
                        var orchestrator = new VerificationOrchestrator(actualWorkspaceRoot);
                        var session = orchestrator.CreateVerifierSession(AppState.SessionId);
                        var checks = new List<VerificationCheck>();

                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            var (buildOut, buildErr, buildExit) = await RunProcessAsync("dotnet build -p:UseAppHost=false", actualWorkspaceRoot);
                            checks.Add(orchestrator.RunCheck("Standard Build", "dotnet build -p:UseAppHost=false", buildOut + "\n" + buildErr, buildExit));

                            var (strictOut, strictErr, strictExit) = await RunProcessAsync("dotnet build -p:UseAppHost=false -p:TreatWarningsAsErrors=true", actualWorkspaceRoot);
                            checks.Add(orchestrator.RunCheck("Strict Nullable Build", "dotnet build -p:UseAppHost=false -p:TreatWarningsAsErrors=true", strictOut + "\n" + strictErr, strictExit));

                            var (testOut, testErr, testExit) = await RunProcessAsync("dotnet test --no-build", actualWorkspaceRoot);
                            checks.Add(orchestrator.RunCheck("Unit Tests", "dotnet test --no-build", testOut + "\n" + testErr, testExit));
                        }
                        else
                        {
                            var (outStr, errStr, exitCode) = await RunProcessAsync(payload, actualWorkspaceRoot);
                            checks.Add(orchestrator.RunCheck("Custom Verification", payload, outStr + "\n" + errStr, exitCode));
                        }

                        var verifResult = orchestrator.AggregateResult(session.VerifierSessionId, session.GeneratorSessionId, checks);
                        await orchestrator.WriteResultAsync(verifResult);

                        string relativeResultPath = Path.Combine(".claude4net", "sessions", session.VerifierSessionId, "verification-result.json");
                        record.EvidenceFiles.Add(relativeResultPath);

                        if (verifResult.Verdict == VerificationVerdict.Fail)
                        {
                            throw new InvalidOperationException($"Verification failed with verdict: {verifResult.Verdict}");
                        }
                    }
                }

                record.Success = true;
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.Error = ex.Message;
            }
            finally
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
                AppState.CurrentCwd = oldCwd;

                // 7. Save Run Record
                await _store.SaveRunRecordAsync(record);

                // 8. Event Sourcing
                string actualWorkspaceRoot = workspaceRoot;
                try
                {
                    if (!string.IsNullOrWhiteSpace(routineId) && _store != null)
                    {
                        var loaded = await _store.LoadAsync(routineId);
                        if (loaded != null && !string.IsNullOrWhiteSpace(loaded.WorkspaceRoot))
                        {
                            actualWorkspaceRoot = loaded.WorkspaceRoot;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(actualWorkspaceRoot))
                    {
                        actualWorkspaceRoot = oldCwd ?? Directory.GetCurrentDirectory();
                    }

                    var eventStore = new FileAgentEventStore(actualWorkspaceRoot);
                    await eventStore.AppendEventAsync(
                        AppState.SessionId ?? Guid.NewGuid().ToString("N"),
                        new RoutineRunEvent
                        {
                            Version = 1,
                            RoutineId = routineId,
                            RunId = record.RunId,
                            Success = record.Success,
                            Error = record.Error
                        }
                    );
                }
                catch
                {
                    // Ignore event sourcing failures in finally to avoid hiding original exception
                }

                // 9. After Hooks / On Error Hooks
                if (hookPipeline != null && routineHookCtx != null)
                {
                    if (record.Success)
                    {
                        routineHookCtx.Result = "Success";
                        await hookPipeline.ExecuteAfterAsync(routineHookCtx);
                    }
                    else
                    {
                        routineHookCtx.IsError = true;
                        routineHookCtx.Result = record.Error;
                        await hookPipeline.ExecuteOnErrorAsync(routineHookCtx);
                    }
                }
            }

            return record;
        }

        private static async Task<string> ExecuteSlashCommandReflectionAsync(string cmdName, string arguments, IServiceProvider sp)
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Claude4Net.Commands")
                ?? System.Reflection.Assembly.Load("Claude4Net.Commands");

            var type = assembly.GetType("Claude4Net.Commands.CommandRegistry")
                ?? throw new InvalidOperationException("CommandRegistry class not found.");

            var findCommandMethod = type.GetMethod("FindCommand", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? throw new InvalidOperationException("FindCommand method not found.");

            var commandObj = findCommandMethod.Invoke(null, new object[] { cmdName });
            if (commandObj == null) throw new InvalidOperationException($"Command '{cmdName}' not found.");

            var handlerProperty = commandObj.GetType().GetProperty("Handler", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                ?? throw new InvalidOperationException("Handler property not found.");

            var handlerDelegate = handlerProperty.GetValue(commandObj) as Delegate;
            if (handlerDelegate == null) throw new InvalidOperationException("Handler delegate is null.");

            var resultTask = handlerDelegate.DynamicInvoke(new object[] { arguments, sp }) as Task<string>
                ?? throw new InvalidOperationException("Command handler did not return Task<string>.");

            return await resultTask;
        }

        private async Task<(string output, string error, int exitCode)> RunProcessAsync(string command, string workspaceRoot)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{command}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workspaceRoot
                };
                using var process = Process.Start(psi);
                if (process == null) return ("", "Could not start powershell process.", -1);
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return (output, error, process.ExitCode);
            }
            catch (Exception ex)
            {
                return ("", ex.Message, -1);
            }
        }

        private List<string> GetWorkspaceFilesToBackup(string workspaceRoot)
        {
            var list = new List<string>();
            if (!Directory.Exists(workspaceRoot)) return list;

            var allFiles = Directory.GetFiles(workspaceRoot, "*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var relative = Path.GetRelativePath(workspaceRoot, file);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Contains(".git") || parts.Contains(".claude4net") || parts.Contains("bin") || parts.Contains("obj") || parts.Contains(".gemini"))
                {
                    continue;
                }
                list.Add(relative);
            }
            return list;
        }
    }
}
