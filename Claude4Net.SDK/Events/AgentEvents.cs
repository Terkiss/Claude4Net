using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Claude4Net.SDK.Events
{
    /// <summary>
    /// ?ì´?„íŠ¸??ëª¨ë“  ?´ë²¤?¸ë? ?„í•œ ê¸°ë³¸ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IAgentEvent
    {
        string EventId { get; }
        DateTime Timestamp { get; }
        long Version { get; }
        string EventType { get; }
    }

    /// <summary>
    /// ê³µí†µ ?´ë²¤???ì„±???´ì? ê¸°ë³¸ ?´ë˜?¤ì…?ˆë‹¤.
    /// </summary>
    public abstract class AgentEventBase : IAgentEvent
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public long Version { get; set; }
        public abstract string EventType { get; }
    }

    /// <summary>
    /// ?¸ì…˜ ?œì‘ ?´ë²¤??
    /// </summary>
    public class SessionStartedEvent : AgentEventBase
    {
        public override string EventType => "SessionStarted";
        public string WorkspacePath { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?¬ìš©???…ë ¥ ?˜ì‹  ?´ë²¤??
    /// </summary>
    public class UserPromptReceivedEvent : AgentEventBase
    {
        public override string EventType => "UserPromptReceived";
        public string Prompt { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?ì´?„íŠ¸???¬ê³ (Thinking) ê¸°ë¡ ?´ë²¤??
    /// </summary>
    public class AgentThoughtEvent : AgentEventBase
    {
        public override string EventType => "AgentThought";
        public string Thought { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?„êµ¬ ?¸ì¶œ ?´ë²¤??
    /// </summary>
    public class ToolCalledEvent : AgentEventBase
    {
        public override string EventType => "ToolCalled";
        public string ToolUseId { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?„êµ¬ ?¤í–‰ ê²°ê³¼ ?´ë²¤??
    /// </summary>
    public class ToolResultEvent : AgentEventBase
    {
        public override string EventType => "ToolResult";
        public string ToolUseId { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    /// <summary>
    /// ìµœì¢… ?‘ë‹µ ?ì„± ?´ë²¤??
    /// </summary>
    public class FinalResponseGeneratedEvent : AgentEventBase
    {
        public override string EventType => "FinalResponseGenerated";
        public string Response { get; set; } = string.Empty;
    }

    /// <summary>
    /// ?ì´?„íŠ¸ ?íƒœ ?¤ëƒ…??(?´ë²¤???Œì‹± ?¬ìƒ ìµœì ?”ìš©)
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
