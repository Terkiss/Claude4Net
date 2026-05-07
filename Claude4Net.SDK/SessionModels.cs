using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 세션의 영속적 메타데이터를 담는 레코드입니다. (session.json)
    /// </summary>
    public class AgentSessionRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public PermissionMode PermissionMode { get; set; }
        public string WorkspacePath { get; set; } = string.Empty;
        public string Status { get; set; } = "Active";
        public Dictionary<string, string> Metadata { get; set; } = new();
        public string SchemaVersion { get; set; } = "1.0";
    }

    /// <summary>
    /// 태스크 보드의 전체 상태를 담는 레코드입니다. (task-board.json)
    /// </summary>
    public class AgentTaskBoardRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public List<AgentTaskRecord> Tasks { get; set; } = new();
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
        public string SchemaVersion { get; set; } = "1.0";
    }

    /// <summary>
    /// 개별 태스크의 상태를 담는 레코드입니다.
    /// </summary>
    public class AgentTaskRecord
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed, Blocked
        public string? AssignedAgent { get; set; }
        public double Progress { get; set; } // 0 to 100
        public List<string> Dependencies { get; set; } = new();
        public Dictionary<string, object> ExtraData { get; set; } = new();
    }

    /// <summary>
    /// 에이전트의 진행 상황을 기록하는 이벤트 모델입니다. (progress-{agent}.jsonl)
    /// </summary>
    public class AgentProgressEvent
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string AgentId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Thinking, ToolCall, ToolResult, Text, Error, Info
        public string? Message { get; set; }
        public object? Data { get; set; }
    }
}
