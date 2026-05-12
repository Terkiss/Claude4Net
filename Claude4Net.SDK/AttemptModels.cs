using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    public sealed record AgentTaskAttempt(
        string AttemptId,
        int AttemptNumber,
        string? SessionId,
        string? ProviderId,
        string? ModelId,
        string Status,
        string? Error,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt
    );

    public class AttemptRecord
    {
        public int Sequence { get; set; }
        public string Goal { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
        public DateTime StartedAt { get; set; }
        public long DurationMs { get; set; }
    }

    public sealed record AttemptTimeline(
        string SessionId,
        List<AgentTaskAttempt> Attempts
    );
}
