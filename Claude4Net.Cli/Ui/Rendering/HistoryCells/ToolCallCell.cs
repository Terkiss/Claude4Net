namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolCallCell(string callId, string toolName, string arguments) : HistoryCell
{
    public string CallId { get; } = callId;
    public string ToolName { get; } = toolName;
    public string Arguments { get; } = arguments;

    public override string ToPlainText() => $"Tool Call: {ToolName}({Arguments}) [ID: {CallId}]";
}
