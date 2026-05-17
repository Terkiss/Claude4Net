using System;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Major phases of an agent run for status display.
/// </summary>
public enum LumenRunPhase
{
    Idle,
    Routing,
    Thinking,
    Streaming,
    RunningTool,
    AwaitingApproval,
    Cancelling,
    Error
}

/// <summary>
/// Status of the active or most recent run.
/// </summary>
public sealed record RunStatusState(
    LumenRunPhase Phase,
    string? Message = null,
    string? ActiveToolName = null,
    int ActiveToolCount = 0);

/// <summary>
/// Immutable state of the Lumen footer.
/// </summary>
public sealed record FooterState(
    string Status,
    string? Provider,
    string? Model,
    string? PermissionMode,
    string? SessionId,
    string? Hint,
    string? Notice);
