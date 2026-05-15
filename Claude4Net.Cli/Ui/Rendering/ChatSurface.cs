using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class ChatSurface
{
    public IRenderable Render(IEnumerable<HistoryCell> cells)
    {
        var list = new List<IRenderable>();
        foreach (var cell in cells)
        {
            list.Add(cell.GetRenderable());
            list.Add(new Text(Environment.NewLine));
        }
        return new Rows(list);
    }
}
