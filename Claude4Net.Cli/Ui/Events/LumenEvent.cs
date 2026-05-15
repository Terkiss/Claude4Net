namespace Claude4Net.Cli.Ui.Events;

public abstract record LumenEvent;

public record RunStartedEvent(string Provider, string Model, string SessionId) : LumenEvent;

public record UserPromptSubmittedEvent(string Text) : LumenEvent;

public record ThinkingStartedEvent(string? InitialThought = null) : LumenEvent;

public record ThinkingUpdatedEvent(string ThoughtDelta) : LumenEvent;

public record AssistantTextUpdatedEvent(string TextDelta) : LumenEvent;

public record ToolCallStartedEvent(string CallId, string ToolName, string Arguments) : LumenEvent;

public record ToolResultReceivedEvent(string CallId, string Result, bool IsError = false) : LumenEvent;

public record NoticeReceivedEvent(string Message, string Level = "Info") : LumenEvent;

public record ErrorReceivedEvent(string Message, string? Details = null) : LumenEvent;

public record ApprovalRequestedEvent(string RequestId, string Title, string Description) : LumenEvent;

public record RunCompletedEvent : LumenEvent;
