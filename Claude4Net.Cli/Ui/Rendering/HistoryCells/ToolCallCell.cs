using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolCallCell(string callId, string toolName, string arguments) : HistoryCell
{
    private const int MaxArgsDisplayLength = 1000;
    public string CallId { get; } = callId;
    public string ToolName { get; } = toolName;
    public string Arguments { get; } = arguments;

    public bool IsExpanded { get; set; } = true;

    public override string ToPlainText() => $"Tool Call: {ToolName}({Arguments}) [ID: {CallId}]";

    public override IRenderable GetRenderable()
    {
        var toolColor = LumenTheme.ToolColor;

        if (!IsExpanded)
        {
            return new Markup($"[{toolColor}]+ TOOL CALL:[/] [bold]{Markup.Escape(ToolName)}[/] - Press 'T' to view arguments");
        }

        string displayArgs = Arguments;
        if (displayArgs.Length > MaxArgsDisplayLength)
        {
            displayArgs = displayArgs.Substring(0, MaxArgsDisplayLength) + $"\n... (Truncated. Total length: {Arguments.Length} bytes)";
        }

        return new Markup($"[{toolColor}]- TOOL CALL:[/] [bold]{Markup.Escape(ToolName)}[/]({Markup.Escape(displayArgs)})");
    }
}
