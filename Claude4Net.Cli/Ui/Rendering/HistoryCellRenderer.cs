using System.Collections.Generic;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Renders HistoryCells into DisplayLines.
/// </summary>
public static class HistoryCellRenderer
{
    public static IEnumerable<DisplayLine> Render(HistoryCell cell, int width)
    {
        // Simplistic implementation for now, will be expanded as needed.
        // Each cell type should ideally handle its own rendering logic.

        var lines = TerminalText.WrapByDisplayWidth(cell.ToPlainText(), width);
        foreach (var line in lines)
        {
            yield return new DisplayLine(line, DisplayLineKind.Transcript);
        }
    }
}
