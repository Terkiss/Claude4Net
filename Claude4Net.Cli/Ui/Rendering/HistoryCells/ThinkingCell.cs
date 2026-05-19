using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ThinkingCell(string? initialThought = null) : HistoryCell
{
    private readonly StringBuilder _buffer = new(initialThought ?? string.Empty);

    public string Content => _buffer.ToString();

    public override void AppendDelta(string delta)
    {
        _buffer.Append(delta);
    }

    public override string ToPlainText() => $"Thinking: {Content}";

    public override IRenderable GetRenderable()
    {
        var content = string.IsNullOrWhiteSpace(Content) ? "..." : Content;
        return new Panel(new Markup($"[italic]{Markup.Escape(content)}[/]"))
        {
            Header = new PanelHeader(" THOUGHT "),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Grey35),
            Padding = new Padding(1, 0, 1, 0)
        };
    }
}
