using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public enum AgentRunState
    {
        Idle,
        Routing,
        LoadingContext,
        QueryingModel,
        WaitingApproval,
        ExecutingTool,
        Checkpointing,
        Verifying,
        Recovering,
        Completed,
        Failed,
        Cancelled
    }

    public class AgentRunStateModel
    {
        public string SessionId { get; set; } = string.Empty;
        public AgentRunState CurrentState { get; set; } = AgentRunState.Idle;
        public List<AttemptRecord> Attempts { get; set; } = new();
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed record AgentStateTransition(
        string SessionId,
        AgentRunState FromState,
        AgentRunState ToState,
        string? Reason,
        DateTimeOffset Timestamp
    );
}
