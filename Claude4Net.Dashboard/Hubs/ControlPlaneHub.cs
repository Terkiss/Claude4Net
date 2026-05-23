using Microsoft.AspNetCore.SignalR;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.Dashboard.Client.Models;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

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
}
