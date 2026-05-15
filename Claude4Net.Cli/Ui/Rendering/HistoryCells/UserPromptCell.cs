namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class UserPromptCell(string text) : HistoryCell
{
    public string Text { get; } = text;

    public override string ToPlainText() => $"User: {Text}";
}
