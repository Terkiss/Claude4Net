using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.Cli.Ui.Approval;

namespace Claude4Net.Cli.Ui;

public record LumenState
{
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? SessionId { get; init; }
    public bool IsRunning { get; init; }

    public List<HistoryCell> History { get; init; } = new();

    public HistoryCell? LastCell => History.LastOrDefault();

    public ApprovalDialogState ApprovalDialog { get; init; } = ApprovalDialogState.Hidden;

    public ViewportScrollState Scroll { get; init; } = new(0, true);
}
