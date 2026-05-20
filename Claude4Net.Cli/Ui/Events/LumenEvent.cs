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

// Approval Dialog Events
public record ApprovalDialogOpenedEvent(string RequestId, string Title, string Description, string RiskLevel, string PreviewSummary) : LumenEvent;

public record ApprovalDialogClosedEvent : LumenEvent;

public record ApprovalDialogActionSelectedEvent(string RequestId, Claude4Net.Cli.Ui.Approval.ApprovalDialogAction Action) : LumenEvent;

public record ApprovalDialogDetailToggledEvent : LumenEvent;

// Scroll Events
public record ScrollUpRequestedEvent(int Lines) : LumenEvent;
public record ScrollDownRequestedEvent(int Lines) : LumenEvent;
public record ScrollToHomeRequestedEvent : LumenEvent;
public record ScrollToEndRequestedEvent : LumenEvent;

// TUI Custom Commands Events
public record ClearTranscriptEvent : LumenEvent;
public record ThemeChangedEvent(string ThemeName) : LumenEvent;
public record ModelChangedEvent(string Provider, string Model) : LumenEvent;
