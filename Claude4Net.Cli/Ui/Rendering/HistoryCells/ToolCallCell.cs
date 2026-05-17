using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolCallCell(string callId, string toolName, string arguments) : HistoryCell
{
    private const int MaxArgsDisplayLength = 500;
    public string CallId { get; } = callId;
    public string ToolName { get; } = toolName;
    public string Arguments { get; } = arguments;

    public override string ToPlainText() => $"Tool Call: {ToolName}({Arguments}) [ID: {CallId}]";

    public override IRenderable GetRenderable()
    {
        string displayArgs = Arguments;
        if (displayArgs.Length > MaxArgsDisplayLength)
        {
            displayArgs = displayArgs.Substring(0, MaxArgsDisplayLength) + "...";
        }

        return new Markup($"[{LumenTheme.ToolColor}]TOOL CALL:[/] [bold]{Markup.Escape(ToolName)}[/]({Markup.Escape(displayArgs)})");
    }
}
