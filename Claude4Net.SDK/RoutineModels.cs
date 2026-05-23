using System;
using System.Collections.Generic;
using Claude4Net.SDK; // Assume PermissionMode is here or somewhere known

namespace Claude4Net.SDK
{
    public enum RoutineTriggerKind
    {
        Manual,
        Interval,
        DailyTime,
        Webhook,
        Event
    }

    public enum RoutineActionKind
    {
        SlashCommand,
        Verification,
        Prompt,
        Script
    }

    public sealed class RoutineTrigger
    {
        public RoutineTriggerKind Kind { get; set; }
        public string? Expression { get; set; }
    }

    public sealed class RoutineAction
    {
        public RoutineActionKind Kind { get; set; }
        public string Payload { get; set; } = string.Empty;
    }

    public sealed class RoutineDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public bool IsEnabled { get; set; } = false;
        public bool Enabled
        {
            get => IsEnabled;
            set => IsEnabled = value;
        }

        public RoutineTrigger Trigger { get; set; } = new();
        public List<RoutineAction> Actions { get; set; } = new();

        public PermissionMode PermissionMode { get; set; } = PermissionMode.Prompt;
        public PermissionMode RequiredPermissionMode
        {
            get => PermissionMode;
            set => PermissionMode = value;
        }

        public string? WorkspaceDir { get; set; }
        public string? WorkspaceRoot
        {
            get => WorkspaceDir;
            set => WorkspaceDir = value;
        }

        public DateTimeOffset? LastRun { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class RoutineRunRecord
    {
        public string RunId { get; set; } = string.Empty;
        public string RoutineId { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<string> EvidenceFiles { get; set; } = new();
    }
}
