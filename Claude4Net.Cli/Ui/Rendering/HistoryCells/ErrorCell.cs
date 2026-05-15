namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ErrorCell(string message, string? details = null) : HistoryCell
{
    public string Message { get; } = message;
    public string? Details { get; } = details;

    public override string ToPlainText() => $"ERROR: {Message}{(Details != null ? $" - {Details}" : "")}";
}
