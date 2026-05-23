using System;
using System.Collections.Generic;

namespace Claude4Net.Dashboard.Client.Models
{
    public class ProviderControlPlaneState
    {
        public List<ProviderDescriptorDto> Providers { get; set; } = new();
    }

    public class ProviderDescriptorDto
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string TransportKind { get; set; } = string.Empty;
        public double CostScore { get; set; }
        public int ContextWindowSize { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public string HealthStatus { get; set; } = "Healthy";
        public double LatencyEma { get; set; }
        public int ErrorCount { get; set; }
        public int SuccessCount { get; set; }
    }

    public class CoordinateControlPlaneState
    {
        public List<CoordinateTaskDto> Tasks { get; set; } = new();
    }

    public class CoordinateTaskDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public double ReadinessScore { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
        public List<CoordinateGateDto> Gates { get; set; } = new();
        public List<string> Blockers { get; set; } = new();
        public string? SpecId { get; set; }
    }

    public class CoordinateGateDto
    {
        public string Name { get; set; } = string.Empty;
        public bool IsPassed { get; set; }
        public bool IsEvidenceRequired { get; set; }
        public string? Comments { get; set; }
        public string? ApprovedBy { get; set; }
    }

    public class CheckpointControlPlaneState
    {
        public List<CheckpointManifestDto> Checkpoints { get; set; } = new();
    }

    public class CheckpointManifestDto
    {
        public string Id { get; set; } = string.Empty;
        public string ToolCallId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<string> ChangedFiles { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string? StateSnapshotId { get; set; }
        public bool IncludesMemoryState { get; set; }
    }

    public class VerificationControlPlaneState
    {
        public VerificationResultDto? Result { get; set; }
    }

    public class VerificationResultDto
    {
        public string VerifierSessionId { get; set; } = string.Empty;
        public string? GeneratorSessionId { get; set; }
        public string Verdict { get; set; } = "Fail";
        public List<VerificationCheckDto> Checks { get; set; } = new();
        public DateTimeOffset Timestamp { get; set; }
    }

    public class VerificationCheckDto
    {
        public string Name { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string? OutputFile { get; set; }
        public string Result { get; set; } = "Fail";
        public string? Evidence { get; set; }
        public string? Notes { get; set; }
        public bool Skipped { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
    }

    public class SkillControlPlaneState
    {
        public List<SkillRegistryRecordDto> Skills { get; set; } = new();
        public List<SkillProposalRecordDto> Proposals { get; set; } = new();
    }

    public class SkillRegistryRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new();
        public SkillQualityMetricsDto Metrics { get; set; } = new();
    }

    public class SkillQualityMetricsDto
    {
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double AverageScore { get; set; }
        public DateTime? LastUsed { get; set; }
    }

    public class SkillProposalRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string SkillId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public string ProposedChanges { get; set; } = string.Empty;
        public string Status { get; set; } = "Draft";
        public string TargetPath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class RoutineControlPlaneState
    {
        public List<RoutineDefinitionDto> Routines { get; set; } = new();
    }

    public class RoutineDefinitionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string RequiredPermissionMode { get; set; } = "ReadOnly";
        public string TriggerKind { get; set; } = "Manual";
        public string TriggerExpression { get; set; } = string.Empty;
        public DateTimeOffset? LastRun { get; set; }
        public DateTimeOffset? NextRun { get; set; }
        public string? LastRunStatus { get; set; }
        public List<RoutineActionDto> Actions { get; set; } = new();
        public List<RoutineRunRecordDto> History { get; set; } = new();
    }

    public class RoutineActionDto
    {
        public string Type { get; set; } = string.Empty;
        public string ParametersJson { get; set; } = string.Empty;
    }

    public class RoutineRunRecordDto
    {
        public string RunId { get; set; } = string.Empty;
        public string RoutineId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public string? Error { get; set; }
    }

    public class StateControlPlaneState
    {
        public AgentSessionRecordDto? Session { get; set; }
        public AgentTaskBoardRecordDto? TaskBoard { get; set; }
        public List<MemoryTableDto> MemoryTables { get; set; } = new();
    }

    public class AgentSessionRecordDto
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string PermissionMode { get; set; } = "Default";
        public string WorkspacePath { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    public class AgentTaskBoardRecordDto
    {
        public string SessionId { get; set; } = string.Empty;
        public List<AgentTaskRecordDto> Tasks { get; set; } = new();
        public DateTime LastUpdatedAt { get; set; }
    }

    public class AgentTaskRecordDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending";
        public string? AssignedAgent { get; set; }
        public double Progress { get; set; }
        public List<string> Dependencies { get; set; } = new();
    }

    public class MemoryTableDto
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int RowCount { get; set; }
    }
}
