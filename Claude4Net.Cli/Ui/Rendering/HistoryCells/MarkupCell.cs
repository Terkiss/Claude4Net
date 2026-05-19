using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

/// <summary>
/// A history cell that renders Spectre.Console markup without escaping it.
/// SECURITY: This MUST ONLY be used for [bold]trusted command markup only[/].
/// Untrusted user input or LLM responses should be escaped or handled by other cells.
/// </summary>
public class MarkupCell(string markup) : HistoryCell
{
    public string MarkupText { get; } = markup;

    public override string ToPlainText() => Markup.Remove(MarkupText);

    public override IRenderable GetRenderable()
    {
        // Trusted markup from internal commands
        return new Markup(MarkupText);
    }
}
