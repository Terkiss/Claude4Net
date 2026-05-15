using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class AssistantResponseCell : HistoryCell
{
    private readonly StringBuilder _buffer = new();

    public string Content => _buffer.ToString();

    public override void AppendDelta(string delta)
    {
        _buffer.Append(delta);
    }

    public override string ToPlainText() => $"Assistant: {Content}";

    public override IRenderable GetRenderable()
    {
        return new Markup($"[{LumenTheme.AssistantColor}]ASSISTANT:[/] {Markup.Escape(Content)}");
    }
}
