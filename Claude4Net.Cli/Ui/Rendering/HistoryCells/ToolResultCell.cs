using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolResultCell(string callId, string result, bool isError = false) : HistoryCell
{
    private const int MaxDisplayLength = 1000;
    public string CallId { get; } = callId;
    public string Result { get; } = result;
    public bool IsError { get; } = isError;

    public override string ToPlainText() => $"Tool Result [{CallId}]: {(IsError ? "ERROR: " : "")}{Result}";

    public override IRenderable GetRenderable()
    {
        var color = IsError ? LumenTheme.ErrorColor : LumenTheme.ToolColor;
        var prefix = IsError ? "TOOL ERROR" : "TOOL RESULT";

        string displayResult = Result;
        bool isTruncated = false;

        if (displayResult.Length > MaxDisplayLength)
        {
            displayResult = displayResult.Substring(0, MaxDisplayLength) + "...";
            isTruncated = true;
        }

        var content = new Markup($"[{color}]{prefix}:[/] {Markup.Escape(displayResult)}");

        if (isTruncated)
        {
            return new Rows(
                content,
                new Markup($"[grey](Truncated for display. Length: {Result.Length})[/]")
            );
        }

        return content;
    }
}
