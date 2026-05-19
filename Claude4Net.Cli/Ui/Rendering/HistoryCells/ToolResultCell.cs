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
        var prefix = IsError ? " TOOL ERROR " : " TOOL RESULT ";

        string displayResult = Result;
        bool isTruncated = false;

        if (displayResult.Length > MaxDisplayLength)
        {
            displayResult = displayResult.Substring(0, MaxDisplayLength) + "...";
            isTruncated = true;
        }

        var markup = new Markup(Markup.Escape(displayResult));
        IRenderable content = isTruncated
            ? new Rows(markup, new Markup($"[grey](Truncated. Total length: {Result.Length})[/]"))
            : markup;

        var colorObj = IsError ? Color.Red : Color.Yellow;

        return new Panel(content)
        {
            Header = new PanelHeader($"[{color}]{prefix}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Grey35),
            Padding = new Padding(1, 0, 1, 0)
        };
    }
}
