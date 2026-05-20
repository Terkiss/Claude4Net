using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ToolResultCell(string callId, string result, bool isError = false) : HistoryCell
{
    private const int MaxDisplayLength = 2000;
    public string CallId { get; } = callId;
    public string Result { get; } = result;
    public bool IsError { get; } = isError;

    public bool IsExpanded { get; set; } = true;

    public override string ToPlainText() => $"Tool Result [{CallId}]: {(IsError ? "ERROR: " : "")}{Result}";

    public override IRenderable GetRenderable()
    {
        var color = IsError ? LumenTheme.ErrorColor : LumenTheme.ToolColor;
        var prefix = IsError ? "TOOL ERROR" : "TOOL RESULT";
        var statusSymbol = IsError ? "Failed" : "Success";
        int size = Result?.Length ?? 0;

        if (!IsExpanded)
        {
            return new Markup($"[{color}]+ {prefix}:[/] {statusSymbol} ({size} bytes) - Press 'T' to view details");
        }

        string displayResult = Result ?? string.Empty;
        bool isTruncated = false;

        if (displayResult.Length > MaxDisplayLength)
        {
            displayResult = displayResult.Substring(0, MaxDisplayLength) + "...";
            isTruncated = true;
        }

        var markup = new Markup(Markup.Escape(displayResult));
        IRenderable content = isTruncated
            ? new Rows(markup, new Markup($"[grey](Truncated. Total length: {Result?.Length ?? 0} bytes)[/]"))
            : markup;

        // Parse border color safely from theme
        var borderColor = Color.Grey35;
        try
        {
            borderColor = Style.Parse(LumenTheme.BorderColor).Foreground;
        }
        catch { }

        return new Panel(content)
        {
            Header = new PanelHeader($"- {prefix} ({statusSymbol})"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: borderColor),
            Padding = new Padding(1, 0, 1, 0)
        };
    }
}
