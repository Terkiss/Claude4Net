using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

/// <summary>
/// A history cell that renders a Spectre.Console IRenderable directly.
/// SECURITY: This is used for [bold]trusted command renderables only[/] (e.g., Tables, BarCharts).
/// </summary>
public class RenderableCell(IRenderable renderable) : HistoryCell
{
    public IRenderable Renderable { get; } = renderable;

    public override string ToPlainText() => "Renderable content"; // IRenderables don't have a standard plain text export

    public override IRenderable GetRenderable()
    {
        return Renderable;
    }
}
