namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class NoticeCell(string message, string level = "Info") : HistoryCell
{
    public string Message { get; } = message;
    public string Level { get; } = level;

    public override string ToPlainText() => $"[{Level.ToUpper()}]: {Message}";
}
