using Microsoft.AspNetCore.SignalR;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Dashboard.Client.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Claude4Net.Dashboard.Hubs;

public class ControlPlaneHub : Hub
{
    private readonly IServiceProvider? _serviceProvider;

    public ControlPlaneHub(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<string> ExecuteCommand(string commandLine)
    {
        // P1-1 Security Remediation: Command execution via Dashboard SignalR is disabled.
        // Returning explicit deny to prevent unauthorized remote command execution.
        return Task.FromResult("Execution denied: Remote command execution via Dashboard is disabled for security reasons.");
    }

    private string GetWorkspaceRoot()
    {
        return AppState.CurrentCwd ?? AppState.SystemBaseDir ?? AppDomain.CurrentDomain.BaseDirectory;
    }

    private ISmartRouter GetSmartRouter(string ws)
    {
        var router = _serviceProvider?.GetService(typeof(ISmartRouter)) as ISmartRouter;
        if (router == null)
        {
            var registry = ProviderRegistry.CreateWithDefaults(ws);
            router = new SmartRouter(registry);
        }
        return router;
    }

    public Task<ProviderControlPlaneState> GetProviders()
    {
        try
        {
            string ws = GetWorkspaceRoot();
            var router = GetSmartRouter(ws);
            var registry = (router as SmartRouter)?.Registry ?? ProviderRegistry.CreateWithDefaults(ws);

            var state = new ProviderControlPlaneState();
            var metrics = router.GetMetrics()?.ToDictionary(m => m.ProviderName, m => m, StringComparer.OrdinalIgnoreCase)
                          ?? new Dictionary<string, ProviderMetric>();

            foreach (var descriptor in registry.All)
            {
                metrics.TryGetValue(descriptor.Id, out var metric);
                state.Providers.Add(new ProviderDescriptorDto
                {
                    Id = descriptor.Id,
                    Label = descriptor.Label,
                    TransportKind = descriptor.TransportKind,
                    CostScore = descriptor.CostScore,
                    ContextWindowSize = descriptor.ContextWindowSize,
                    Endpoint = descriptor.Endpoint ?? string.Empty,
                    HealthStatus = metric?.Status.ToString() ?? "Healthy",
                    LatencyEma = metric?.LatencyEma ?? 0,
                    ErrorCount = metric?.ErrorCount ?? 0,
                    SuccessCount = metric?.SuccessCount ?? 0
                });
            }
            return Task.FromResult(state);
        }
        catch (Exception)
        {
            return Task.FromResult(new ProviderControlPlaneState());
        }
    }

    public Task<CoordinateControlPlaneState> GetCoordinateTasks()
    {
        try
        {
            var state = new CoordinateControlPlaneState();
            var tasks = AppState.GetCoordinatedTasks();
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    state.Tasks.Add(new CoordinateTaskDto
                    {
                        Id = task.Id,
                        Title = task.Title,
                        Description = task.Description,
                        Status = task.Status,
                        CurrentPhase = task.CurrentPhase.ToString(),
                        ReadinessScore = task.ReadinessScore,
                        ReviewStatus = task.ReviewStatus.ToString(),
                        SpecId = task.SpecId,
                        Blockers = task.Blockers?.ToList() ?? new List<string>(),
                        Gates = task.Gates?.Select(g => new CoordinateGateDto
                        {
                            Name = g.Name,
                            IsPassed = g.IsPassed,
                            IsEvidenceRequired = g.IsEvidenceRequired,
                            Comments = g.Comments,
                            ApprovedBy = g.ApprovedBy
                        }).ToList() ?? new List<CoordinateGateDto>()
                    });
                }
            }
            return Task.FromResult(state);
        }
        catch (Exception)
        {
            return Task.FromResult(new CoordinateControlPlaneState());
        }
    }

    public async Task<CheckpointControlPlaneState> GetCheckpoints(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            // Validate sessionId input to avoid traversal
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new CheckpointControlPlaneState();
            }

            string ws = GetWorkspaceRoot();
            var checkpointStore = new CheckpointStore(ws, sessionId);
            var manifests = await checkpointStore.ListCheckpointsAsync();

            var state = new CheckpointControlPlaneState();
            if (manifests != null)
            {
                foreach (var manifest in manifests)
                {
                    state.Checkpoints.Add(new CheckpointManifestDto
                    {
                        Id = manifest.Id,
                        ToolCallId = manifest.ToolCallId,
                        ToolName = manifest.ToolName,
                        Description = manifest.Description,
                        ChangedFiles = manifest.ChangedFiles?.ToList() ?? new List<string>(),
                        CreatedAt = manifest.CreatedAt,
                        Provider = manifest.Provider ?? string.Empty,
                        Model = manifest.Model ?? string.Empty,
                        StateSnapshotId = manifest.StateSnapshotId,
                        IncludesMemoryState = manifest.IncludesMemoryState
                    });
                }
            }
            return state;
        }
        catch (Exception)
        {
            return new CheckpointControlPlaneState();
        }
    }

    public async Task<VerificationControlPlaneState> GetVerification(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            // Validate sessionId
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new VerificationControlPlaneState();
            }

            string ws = GetWorkspaceRoot();
            var sessionStore = new AgentSessionStore(ws, sessionId);
            var result = await sessionStore.LoadVerificationResultAsync();

            var state = new VerificationControlPlaneState();
            if (result != null)
            {
                state.Result = new VerificationResultDto
                {
                    VerifierSessionId = result.VerifierSessionId,
                    GeneratorSessionId = result.GeneratorSessionId,
                    Verdict = result.Verdict.ToString(),
                    Timestamp = result.Timestamp,
                    Checks = result.Checks?.Select(c => new VerificationCheckDto
                    {
                        Name = c.Name,
                        Command = c.Command,
                        OutputFile = c.OutputFile,
                        Result = c.Result.ToString(),
                        Evidence = c.Evidence,
                        Notes = c.Notes,
                        Skipped = c.Skipped,
                        StartedAt = c.StartedAt,
                        CompletedAt = c.CompletedAt
                    }).ToList() ?? new List<VerificationCheckDto>()
                };
            }
            return state;
        }
        catch (Exception)
        {
            return new VerificationControlPlaneState();
        }
    }

    public async Task<SkillControlPlaneState> GetSkills()
    {
        try
        {
            string ws = GetWorkspaceRoot();

            // Resolve registry service
            var registry = _serviceProvider?.GetService(typeof(SkillRegistryService)) as SkillRegistryService;
            if (registry == null)
            {
                registry = new SkillRegistryService(ws);
            }
            await registry.LoadAsync();

            // Resolve proposal service
            var proposalService = _serviceProvider?.GetService(typeof(SkillProposalService)) as SkillProposalService;
            if (proposalService == null)
            {
                proposalService = new SkillProposalService(registry);
            }
            await proposalService.LoadAsync(ws);

            var state = new SkillControlPlaneState();
            var skills = registry.ListSkills();
            if (skills != null)
            {
                foreach (var skill in skills)
                {
                    state.Skills.Add(new SkillRegistryRecordDto
                    {
                        Id = skill.Id,
                        DisplayName = skill.DisplayName,
                        SourcePath = skill.SourcePath,
                        Aliases = skill.Aliases?.ToList() ?? new List<string>(),
                        Metrics = new SkillQualityMetricsDto
                        {
                            SuccessCount = skill.Metrics?.SuccessCount ?? 0,
                            FailureCount = skill.Metrics?.FailureCount ?? 0,
                            AverageScore = skill.Metrics?.AverageScore ?? 0,
                            LastUsed = skill.Metrics?.LastUsed
                        }
                    });
                }
            }

            var proposals = proposalService.ListProposals();
            if (proposals != null)
            {
                foreach (var prop in proposals)
                {
                    state.Proposals.Add(new SkillProposalRecordDto
                    {
                        Id = prop.Id,
                        SkillId = prop.SkillId ?? string.Empty,
                        Title = prop.Title,
                        Description = prop.Description,
                        Rationale = prop.Rationale,
                        ProposedChanges = prop.ProposedChanges,
                        Status = prop.Status.ToString(),
                        TargetPath = prop.TargetPath ?? string.Empty,
                        CreatedAt = prop.CreatedAt,
                        UpdatedAt = prop.UpdatedAt
                    });
                }
            }

            return state;
        }
        catch (Exception)
        {
            return new SkillControlPlaneState();
        }
    }

    public Task<RoutineControlPlaneState> GetRoutines()
    {
        try
        {
            string ws = GetWorkspaceRoot();
            var store = new RoutineStore(ws);

            var state = new RoutineControlPlaneState();
            var routines = store.ListRoutines();
            if (routines != null)
            {
                foreach (var routine in routines)
                {
                    var runRecords = store.GetRunRecords(routine.Id);
                    var history = new List<RoutineRunRecordDto>();
                    string? lastRunStatus = null;

                    if (runRecords != null)
                    {
                        var sortedRuns = runRecords.OrderByDescending(r => r.StartedAt).ToList();
                        if (sortedRuns.Any())
                        {
                            lastRunStatus = sortedRuns.First().Success ? "Success" : "Failed";
                        }

                        foreach (var run in sortedRuns)
                        {
                            history.Add(new RoutineRunRecordDto
                            {
                                RunId = run.RunId,
                                RoutineId = run.RoutineId,
                                Status = run.Success ? "Success" : "Failed",
                                StartedAt = run.StartedAt,
                                CompletedAt = run.CompletedAt,
                                Error = run.Error
                            });
                        }
                    }

                    state.Routines.Add(new RoutineDefinitionDto
                    {
                        Id = routine.Id,
                        Name = routine.Name,
                        Description = string.Empty, // RoutineDefinition has no Description property
                        Enabled = routine.Enabled,
                        RequiredPermissionMode = routine.RequiredPermissionMode.ToString(),
                        TriggerKind = routine.Trigger?.Kind.ToString() ?? "Manual",
                        TriggerExpression = routine.Trigger?.Expression ?? string.Empty,
                        LastRun = routine.LastRun,
                        NextRun = routine.NextRun,
                        LastRunStatus = lastRunStatus,
                        Actions = routine.Actions?.Select(a => new RoutineActionDto
                        {
                            Type = a.Kind.ToString(),
                            ParametersJson = a.Payload
                        }).ToList() ?? new List<RoutineActionDto>(),
                        History = history
                    });
                }
            }
            return Task.FromResult(state);
        }
        catch (Exception)
        {
            return Task.FromResult(new RoutineControlPlaneState());
        }
    }

    public async Task<StateControlPlaneState> GetState(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            // Validate sessionId
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new StateControlPlaneState();
            }

            string ws = GetWorkspaceRoot();

            // 1. Session Load
            var sessionRecord = await AgentSessionStore.LoadSessionRecordAsync(ws, sessionId);
            AgentSessionRecordDto? sessionDto = null;
            if (sessionRecord != null)
            {
                sessionDto = new AgentSessionRecordDto
                {
                    SessionId = sessionRecord.SessionId,
                    StartTime = sessionRecord.StartTime,
                    Provider = sessionRecord.Provider,
                    Model = sessionRecord.Model,
                    PermissionMode = sessionRecord.PermissionMode.ToString(),
                    WorkspacePath = sessionRecord.WorkspacePath,
                    Status = sessionRecord.Status,
                    Metadata = sessionRecord.Metadata ?? new Dictionary<string, string>()
                };
            }

            // 2. Task Board Load
            var sessionStore = new AgentSessionStore(ws, sessionId);
            var taskBoard = await sessionStore.LoadTaskBoardAsync();
            AgentTaskBoardRecordDto? taskBoardDto = null;
            if (taskBoard != null)
            {
                taskBoardDto = new AgentTaskBoardRecordDto
                {
                    SessionId = taskBoard.SessionId,
                    LastUpdatedAt = taskBoard.LastUpdatedAt,
                    Tasks = taskBoard.Tasks?.Select(t => new AgentTaskRecordDto
                    {
                        Id = t.Id,
                        Title = t.Title,
                        Description = t.Description,
                        Status = t.Status,
                        AssignedAgent = t.AssignedAgent,
                        Progress = t.Progress,
                        Dependencies = t.Dependencies?.ToList() ?? new List<string>()
                    }).ToList() ?? new List<AgentTaskRecordDto>()
                };
            }

            // 3. Session Memory Tables Load
            var memoryTables = new List<MemoryTableDto>();
            try
            {
                var targetContext = new WorkspaceStateContext
                {
                    WorkspaceRoot = ws,
                    SessionId = sessionId
                };
                var universeStore = PandasUniverseManager.Instance.GetStore(targetContext);
                if (universeStore != null)
                {
                    var tableNames = universeStore.TableNames;
                    if (tableNames != null)
                    {
                        foreach (var name in tableNames)
                        {
                            int rowCount = await universeStore.ExecuteAsync(u => u.ContainsTable(name) ? u.GetTableOrThrow(name).RowCount : 0);
                            string description = await universeStore.ExecuteAsync(u => u.ContainsTable(name) ? u.GetMetadata(name)?.Description ?? string.Empty : string.Empty);
                            memoryTables.Add(new MemoryTableDto
                            {
                                Name = name,
                                Description = description,
                                RowCount = rowCount
                            });
                        }
                    }
                }
            }
            catch
            {
                // Soft fail for memory DB loading
            }

            return new StateControlPlaneState
            {
                Session = sessionDto,
                TaskBoard = taskBoardDto,
                MemoryTables = memoryTables
            };
        }
        catch (Exception)
        {
            return new StateControlPlaneState();
        }
    }

    private bool IsApprovalCapable(PermissionMode mode)
    {
        var normalized = PermissionEnforcer.Normalize(mode);
        return normalized != PermissionMode.ReadOnly;
    }

    private async Task<(string output, string error, int exitCode)> RunProcessAsync(string command, string workspaceRoot)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workspaceRoot
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null) return (string.Empty, "Could not start powershell process.", -1);

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (outputTask.Result, errorTask.Result, process.ExitCode);
        }
        catch (Exception ex)
        {
            return (string.Empty, ex.Message, -1);
        }
    }

    public async Task<CommandResult> RunRoutine(string routineId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(routineId))
            {
                return new CommandResult { Success = false, Error = "Routine ID cannot be empty." };
            }

            // ID Validation to prevent path traversal/injection
            if (!Regex.IsMatch(routineId, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new CommandResult { Success = false, Error = "Invalid Routine ID format." };
            }

            string ws = GetWorkspaceRoot();
            var store = _serviceProvider?.GetService(typeof(RoutineStore)) as RoutineStore ?? new RoutineStore(ws);
            var runner = _serviceProvider?.GetService(typeof(RoutineRunner)) as RoutineRunner
                         ?? new RoutineRunner(store, new PermissionEnforcer(), new PathSafetyEvaluator(), _serviceProvider);

            var record = await runner.RunAsync(routineId, ws, AppState.CurrentPermissionMode);

            if (record.Success)
            {
                return new CommandResult { Success = true, Message = $"Routine '{routineId}' executed successfully." };
            }
            else
            {
                return new CommandResult { Success = false, Error = record.Error ?? "Unknown routine execution error." };
            }
        }
        catch (Exception ex)
        {
            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CommandResult> RestoreCheckpoint(string checkpointId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(checkpointId))
            {
                return new CommandResult { Success = false, Error = "Checkpoint ID cannot be empty." };
            }

            if (!IsApprovalCapable(AppState.CurrentPermissionMode))
            {
                return new CommandResult { Success = false, Error = "Permission denied: Restore action requires an approval-capable permission mode." };
            }

            if (!Regex.IsMatch(checkpointId, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new CommandResult { Success = false, Error = "Invalid Checkpoint ID format." };
            }

            string ws = GetWorkspaceRoot();
            string sessionId = AppState.SessionId;
            var checkpointStore = new CheckpointStore(ws, sessionId);

            await checkpointStore.RestoreCheckpointAsync(checkpointId);

            // Event Sourcing
            var eventStore = new FileAgentEventStore(ws);
            await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
            {
                CommandName = "RestoreCheckpoint",
                TargetId = checkpointId,
                Success = true,
                Message = $"Checkpoint '{checkpointId}' successfully restored."
            });

            return new CommandResult { Success = true, Message = $"Checkpoint '{checkpointId}' successfully restored." };
        }
        catch (Exception ex)
        {
            try
            {
                string ws = GetWorkspaceRoot();
                var eventStore = new FileAgentEventStore(ws);
                await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
                {
                    CommandName = "RestoreCheckpoint",
                    TargetId = checkpointId,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch {}

            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CommandResult> ApproveSkillProposal(string proposalId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(proposalId))
            {
                return new CommandResult { Success = false, Error = "Proposal ID cannot be empty." };
            }

            if (!IsApprovalCapable(AppState.CurrentPermissionMode))
            {
                return new CommandResult { Success = false, Error = "Permission denied: Approve action requires an approval-capable permission mode." };
            }

            if (!Regex.IsMatch(proposalId, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new CommandResult { Success = false, Error = "Invalid Proposal ID format." };
            }

            string ws = GetWorkspaceRoot();
            var registry = _serviceProvider?.GetService(typeof(SkillRegistryService)) as SkillRegistryService ?? new SkillRegistryService(ws);
            var proposalService = _serviceProvider?.GetService(typeof(SkillProposalService)) as SkillProposalService ?? new SkillProposalService(registry);

            await proposalService.ApproveProposalAsync(ws, proposalId);

            // Event Sourcing
            var eventStore = new FileAgentEventStore(ws);
            await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
            {
                CommandName = "ApproveSkillProposal",
                TargetId = proposalId,
                Success = true,
                Message = $"Skill proposal '{proposalId}' approved."
            });

            return new CommandResult { Success = true, Message = $"Skill proposal '{proposalId}' approved successfully." };
        }
        catch (Exception ex)
        {
            try
            {
                string ws = GetWorkspaceRoot();
                var eventStore = new FileAgentEventStore(ws);
                await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
                {
                    CommandName = "ApproveSkillProposal",
                    TargetId = proposalId,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch {}

            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CommandResult> RejectSkillProposal(string proposalId, string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(proposalId))
            {
                return new CommandResult { Success = false, Error = "Proposal ID cannot be empty." };
            }

            if (!IsApprovalCapable(AppState.CurrentPermissionMode))
            {
                return new CommandResult { Success = false, Error = "Permission denied: Reject action requires an approval-capable permission mode." };
            }

            if (!Regex.IsMatch(proposalId, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new CommandResult { Success = false, Error = "Invalid Proposal ID format." };
            }

            string ws = GetWorkspaceRoot();
            var registry = _serviceProvider?.GetService(typeof(SkillRegistryService)) as SkillRegistryService ?? new SkillRegistryService(ws);
            var proposalService = _serviceProvider?.GetService(typeof(SkillProposalService)) as SkillProposalService ?? new SkillProposalService(registry);

            await proposalService.LoadAsync(ws);
            var proposal = proposalService.GetProposal(proposalId);
            if (proposal != null)
            {
                proposal.Metadata["RejectionReason"] = reason ?? string.Empty;
                await proposalService.SaveAsync(ws);
            }

            await proposalService.RejectProposalAsync(ws, proposalId);

            // Event Sourcing
            var eventStore = new FileAgentEventStore(ws);
            await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
            {
                CommandName = "RejectSkillProposal",
                TargetId = proposalId,
                Success = true,
                Message = $"Skill proposal '{proposalId}' rejected. Reason: {reason}"
            });

            return new CommandResult { Success = true, Message = $"Skill proposal '{proposalId}' rejected successfully." };
        }
        catch (Exception ex)
        {
            try
            {
                string ws = GetWorkspaceRoot();
                var eventStore = new FileAgentEventStore(ws);
                await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
                {
                    CommandName = "RejectSkillProposal",
                    TargetId = proposalId,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch {}

            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CommandResult> ApplySkillProposal(string proposalId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(proposalId))
            {
                return new CommandResult { Success = false, Error = "Proposal ID cannot be empty." };
            }

            if (!IsApprovalCapable(AppState.CurrentPermissionMode))
            {
                return new CommandResult { Success = false, Error = "Permission denied: Apply action requires an approval-capable permission mode." };
            }

            if (!Regex.IsMatch(proposalId, @"^[a-zA-Z0-9\-_]+$"))
            {
                return new CommandResult { Success = false, Error = "Invalid Proposal ID format." };
            }

            string ws = GetWorkspaceRoot();
            var registry = _serviceProvider?.GetService(typeof(SkillRegistryService)) as SkillRegistryService ?? new SkillRegistryService(ws);
            var proposalService = _serviceProvider?.GetService(typeof(SkillProposalService)) as SkillProposalService ?? new SkillProposalService(registry);
            await registry.LoadAsync();
            await proposalService.LoadAsync(ws);

            var approvalHandler = _serviceProvider?.GetService(typeof(IRichApprovalHandler)) as IRichApprovalHandler;
            var engine = new SkillApplyEngine(proposalService, registry, approvalHandler);

            bool success = await engine.ApplyAsync(proposalId, ws);

            // Event Sourcing
            var eventStore = new FileAgentEventStore(ws);
            await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
            {
                CommandName = "ApplySkillProposal",
                TargetId = proposalId,
                Success = success,
                Message = success ? "Skill proposal applied successfully." : "Skill proposal application failed post-apply verification and was rolled back."
            });

            if (success)
            {
                return new CommandResult { Success = true, Message = "Skill proposal applied and verified successfully." };
            }
            else
            {
                return new CommandResult { Success = false, Error = "Skill proposal application failed post-apply verification and was rolled back." };
            }
        }
        catch (Exception ex)
        {
            try
            {
                string ws = GetWorkspaceRoot();
                var eventStore = new FileAgentEventStore(ws);
                await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
                {
                    CommandName = "ApplySkillProposal",
                    TargetId = proposalId,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch {}

            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<CommandResult> RunVerification(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new CommandResult { Success = false, Error = "Invalid session ID." };
            }

            string ws = GetWorkspaceRoot();
            var orchestrator = new VerificationOrchestrator(ws);
            var session = orchestrator.CreateVerifierSession(sessionId);
            var checks = new List<VerificationCheck>();

            // Run build
            var (buildOut, buildErr, buildExit) = await RunProcessAsync("dotnet build -p:UseAppHost=false", ws);
            checks.Add(orchestrator.RunCheck("Standard Build", "dotnet build -p:UseAppHost=false", buildOut + "\n" + buildErr, buildExit));

            // Run tests
            var (testOut, testErr, testExit) = await RunProcessAsync("dotnet test --no-build", ws);
            checks.Add(orchestrator.RunCheck("Unit Tests", "dotnet test --no-build", testOut + "\n" + testErr, testExit));

            var verifResult = orchestrator.AggregateResult(session.VerifierSessionId, session.GeneratorSessionId, checks);
            await orchestrator.WriteResultAsync(verifResult);

            bool success = verifResult.Verdict == VerificationVerdict.Pass;

            // Event Sourcing
            var eventStore = new FileAgentEventStore(ws);
            await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
            {
                CommandName = "RunVerification",
                TargetId = sessionId,
                Success = success,
                Message = $"Verification finished with verdict: {verifResult.Verdict}"
            });

            await eventStore.AppendEventAsync(AppState.SessionId, new VerificationCompletedEvent
            {
                VerifierSessionId = verifResult.VerifierSessionId,
                GeneratorSessionId = verifResult.GeneratorSessionId,
                Verdict = verifResult.Verdict.ToString(),
                PassedChecks = checks.Count(c => c.Result == VerificationVerdict.Pass),
                TotalChecks = checks.Count
            });

            if (success)
            {
                return new CommandResult { Success = true, Message = $"Verification passed. Verdict: {verifResult.Verdict}." };
            }
            else
            {
                return new CommandResult { Success = false, Error = $"Verification failed. Verdict: {verifResult.Verdict}." };
            }
        }
        catch (Exception ex)
        {
            try
            {
                string ws = GetWorkspaceRoot();
                var eventStore = new FileAgentEventStore(ws);
                await eventStore.AppendEventAsync(AppState.SessionId, new DashboardCommandEvent
                {
                    CommandName = "RunVerification",
                    TargetId = sessionId,
                    Success = false,
                    Error = ex.Message
                });
            }
            catch {}

            return new CommandResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<UsageReadModelDto> GetUsage(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            // Validate sessionId
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new UsageReadModelDto();
            }

            string ws = GetWorkspaceRoot();
            var eventStore = new FileAgentEventStore(ws);
            var projectionEngine = new EventProjectionEngine(eventStore);
            var usageProjection = new UsageProjection();
            projectionEngine.RegisterProjection(usageProjection);
            await projectionEngine.RebuildAsync(sessionId);

            var model = usageProjection.Model;
            var dto = new UsageReadModelDto
            {
                SessionId = sessionId,
                TotalCalls = model.TotalCalls,
                TotalInputTokens = model.TotalInputTokens,
                TotalOutputTokens = model.TotalOutputTokens,
                TotalCost = model.TotalCost,
                LatencyEma = model.LatencyEma,
                ModelMetrics = model.ModelMetrics.Values.Select(m => new ModelUsageMetricsDto
                {
                    Provider = m.Provider,
                    Model = m.Model,
                    CallCount = m.CallCount,
                    InputTokens = m.InputTokens,
                    OutputTokens = m.OutputTokens,
                    LatencyEma = m.LatencyEma,
                    AccumulatedCost = m.AccumulatedCost
                }).ToList()
            };
            return dto;
        }
        catch (Exception)
        {
            return new UsageReadModelDto();
        }
    }

    public async Task<List<AgentSessionRecordDto>> GetSessions()
    {
        try
        {
            string ws = GetWorkspaceRoot();
            string sessionBaseDir = Path.Combine(ws, ".claude4net", "sessions");
            if (!Directory.Exists(sessionBaseDir))
            {
                return new List<AgentSessionRecordDto>();
            }

            var dirs = Directory.GetDirectories(sessionBaseDir);
            var result = new List<AgentSessionRecordDto>();
            foreach (var dir in dirs)
            {
                string sessionId = Path.GetFileName(dir);
                // Validate sessionId path traversal
                if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
                {
                    continue;
                }

                var record = await AgentSessionStore.LoadSessionRecordAsync(ws, sessionId);
                if (record != null)
                {
                    result.Add(new AgentSessionRecordDto
                    {
                        SessionId = record.SessionId,
                        StartTime = record.StartTime,
                        Provider = record.Provider,
                        Model = record.Model,
                        PermissionMode = record.PermissionMode.ToString(),
                        WorkspacePath = record.WorkspacePath,
                        Status = record.Status,
                        Metadata = record.Metadata ?? new Dictionary<string, string>()
                    });
                }
                else
                {
                    result.Add(new AgentSessionRecordDto
                    {
                        SessionId = sessionId,
                        StartTime = Directory.GetCreationTime(dir),
                        Provider = "Unknown",
                        Model = "Unknown",
                        PermissionMode = "Unknown",
                        WorkspacePath = ws,
                        Status = "Inactive",
                        Metadata = new Dictionary<string, string>()
                    });
                }
            }

            return result.OrderByDescending(r => r.StartTime).ToList();
        }
        catch (Exception)
        {
            return new List<AgentSessionRecordDto>();
        }
    }

    public async Task<List<ReplayEventDto>> GetSessionEvents(string sessionId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return new List<ReplayEventDto>();
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new List<ReplayEventDto>();
            }

            string ws = GetWorkspaceRoot();
            var eventStore = new FileAgentEventStore(ws);
            var events = await eventStore.GetEventsAsync(sessionId, 0);

            var result = new List<ReplayEventDto>();
            foreach (var ev in events.OrderBy(e => e.Version))
            {
                string summary = GetEventSummary(ev);
                string payloadJson = System.Text.Json.JsonSerializer.Serialize(ev, ev.GetType(), new System.Text.Json.JsonSerializerOptions { WriteIndented = false });

                result.Add(new ReplayEventDto
                {
                    EventType = ev.EventType,
                    Version = ev.Version,
                    Timestamp = ev.Timestamp,
                    Summary = summary,
                    PayloadJson = payloadJson
                });
            }

            return result;
        }
        catch (Exception)
        {
            return new List<ReplayEventDto>();
        }
    }

    private string GetEventSummary(IAgentEvent ev)
    {
        return ev switch
        {
            SessionStartedEvent started => $"Session started in {started.WorkspacePath} using {started.Provider}/{started.Model}",
            UserPromptReceivedEvent prompt => $"User prompt received: {prompt.Prompt}",
            AgentThoughtEvent thought => $"Agent thought: {thought.Thought}",
            ToolCalledEvent toolCall => $"Tool called: {toolCall.ToolName} with {toolCall.Arguments}",
            ToolResultEvent toolResult => $"Tool result received (Error: {toolResult.IsError}): {toolResult.Result}",
            FinalResponseGeneratedEvent response => $"Final response generated: {response.Response}",
            StateTransitionEvent transition => $"State transitioned from {transition.FromState} to {transition.ToState} (Reason: {transition.Reason})",
            TaskAttemptStartedEvent taskStart => $"Task attempt {taskStart.AttemptNumber} started (AttemptId: {taskStart.AttemptId})",
            TaskAttemptCompletedEvent taskEnd => $"Task attempt {taskEnd.AttemptId} completed with status {taskEnd.Status} (Error: {taskEnd.Error})",
            VerificationCompletedEvent verification => $"Verification completed with verdict: {verification.Verdict} ({verification.PassedChecks}/{verification.TotalChecks} passed)",
            RoutineRunEvent routine => $"Routine run: {routine.RoutineId} (Success: {routine.Success}, Error: {routine.Error})",
            DashboardCommandEvent command => $"Dashboard command: {command.CommandName} for {command.TargetId} (Success: {command.Success})",
            _ => $"Event {ev.EventType} received"
        };
    }

    public async Task<ReconstructedStateDto> ReconstructState(string sessionId, int eventCount)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return new ReconstructedStateDto();
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return new ReconstructedStateDto();
            }

            string ws = GetWorkspaceRoot();
            var eventStore = new FileAgentEventStore(ws);
            var allEvents = (await eventStore.GetEventsAsync(sessionId, 0)).OrderBy(e => e.Version).ToList();

            var filteredEvents = allEvents.Take(eventCount);
            var reconstructed = AgentStateReconstructor.Reconstruct(filteredEvents);

            var historyStrings = new List<string>();
            foreach (var h in reconstructed.History)
            {
                historyStrings.Add(System.Text.Json.JsonSerializer.Serialize(h, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }

            return new ReconstructedStateDto
            {
                HistoryJson = historyStrings,
                CurrentTask = reconstructed.CurrentTask,
                LastVersion = reconstructed.LastVersion
            };
        }
        catch (Exception)
        {
            return new ReconstructedStateDto();
        }
    }
}

public class UsageReadModelDto
{
    public string SessionId { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public double TotalCost { get; set; }
    public double LatencyEma { get; set; }
    public List<ModelUsageMetricsDto> ModelMetrics { get; set; } = new();
}

public class ModelUsageMetricsDto
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double LatencyEma { get; set; }
    public double AccumulatedCost { get; set; }
}
