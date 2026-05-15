using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class UserPromptCell(string text) : HistoryCell
{
    public string Text { get; } = text;

    public override string ToPlainText() => $"User: {Text}";

    public override IRenderable GetRenderable()
    {
        return new Markup($"[{LumenTheme.UserColor}]YOU:[/] {Markup.Escape(Text)}");
    }
}
