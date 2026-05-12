using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Claude4Net.SDK;

namespace Claude4Net.SDK.Events
{
    /// <summary>
    /// ?�이?�트??모든 ?�벤?��? ?�한 기본 ?�터?�이?�입?�다.
    /// </summary>
    public interface IAgentEvent
    {
        string EventId { get; }
        DateTime Timestamp { get; }
        long Version { get; }
        string EventType { get; }
    }

    /// <summary>
    /// 공통 ?�벤???�성???��? 기본 ?�래?�입?�다.
    /// </summary>
    public abstract class AgentEventBase : IAgentEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public long Version { get; set; }
        public abstract string EventType { get; }
    }

    /// <summary>
    /// ?�션 ?�작 ?�벤??
    /// </summary>
    public class SessionStartedEvent : AgentEventBase
    {
        public override string EventType => "SessionStarted";
        public string WorkspacePath { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?�용???�력 ?�신 ?�벤??
    /// </summary>
    public class UserPromptReceivedEvent : AgentEventBase
    {
        public override string EventType => "UserPromptReceived";
        public string Prompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?�이?�트???�고(Thinking) 기록 ?�벤??
    /// </summary>
    public class AgentThoughtEvent : AgentEventBase
    {
        public override string EventType => "AgentThought";
        public string Thought { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?�구 ?�출 ?�벤??
    /// </summary>
    public class ToolCalledEvent : AgentEventBase
    {
        public override string EventType => "ToolCalled";
        public string ToolUseId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?�구 ?�행 결과 ?�벤??
    /// </summary>
    public class ToolResultEvent : AgentEventBase
    {
        public override string EventType => "ToolResult";
        public string ToolUseId { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    /// <summary>
    /// 최종 ?답 ?성 ?벤??
    /// </summary>
    public class FinalResponseGeneratedEvent : AgentEventBase
    {
        public override string EventType => "FinalResponseGenerated";
        public string Response { get; set; } = string.Empty;
    }

    /// <summary>
    /// 상태 전이 이벤트
    /// </summary>
    public class StateTransitionEvent : AgentEventBase
    {
        public override string EventType => "StateTransition";
        public AgentRunState FromState { get; set; }
        public AgentRunState ToState { get; set; }
        public string? Reason { get; set; }
    }

    /// <summary>
    /// 작업 시도 시작 이벤트
    /// </summary>
    public class TaskAttemptStartedEvent : AgentEventBase
    {
        public override string EventType => "TaskAttemptStarted";
        public string AttemptId { get; set; } = string.Empty;
        public int AttemptNumber { get; set; }
        public string? ProviderId { get; set; }
        public string? ModelId { get; set; }
    }

    /// <summary>
    /// 작업 시도 완료 이벤트
    /// </summary>
    public class TaskAttemptCompletedEvent : AgentEventBase
    {
        public override string EventType => "TaskAttemptCompleted";
        public string AttemptId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Error { get; set; }
    }

    /// <summary>
    /// ?이?트 ?태 ?냅??(?벤???싱 ?생 최적?용)

    /// </summary>
    public class AgentStateSnapshot
    {
        public string SessionId { get; set; } = string.Empty;
        public long LastVersion { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<object> History { get; set; } = new();
        public string CurrentTask { get; set; } = string.Empty;
    }
}
