namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolResultCell(string callId, string result, bool isError = false) : HistoryCell
{
    public string CallId { get; } = callId;
    public string Result { get; } = result;
    public bool IsError { get; } = isError;

    public override string ToPlainText() => $"Tool Result [{CallId}]: {(IsError ? "ERROR: " : "")}{Result}";
}
