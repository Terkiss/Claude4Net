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
        return new Markup($"[{LumenTheme.ThinkingColor}]THOUGHT:[/] [italic]{Markup.Escape(Content)}[/]");
    }
}
