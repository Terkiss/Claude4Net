using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class LumenRenderer(IAnsiConsole console)
{
    private readonly ChatSurface _chatSurface = new();
    private readonly FooterRenderer _footerRenderer = new();
    private readonly BottomPane _bottomPane = new();
    private readonly DialogLayer _dialogLayer = new();

    private int _lastRenderedHistoryCount = 0;

    /// <summary>
    /// Renders the entire state. Use this for initial draw.
    /// </summary>
    public void RenderFull(LumenState state)
    {
        console.Write(_chatSurface.Render(state.History));
        console.Write(_bottomPane.Render(state));
        console.Write(Environment.NewLine);
        console.Write(_footerRenderer.Render(state));
        _lastRenderedHistoryCount = state.History.Count;
    }

    /// <summary>
    /// Renders only the new parts of the state since the last render.
    /// This is "scrollback-friendly" as it only appends to the console.
    /// </summary>
    public void RenderAppend(LumenState state)
    {
        if (state.History.Count > _lastRenderedHistoryCount)
        {
            var newCells = state.History.Skip(_lastRenderedHistoryCount);
            console.Write(_chatSurface.Render(newCells));
            _lastRenderedHistoryCount = state.History.Count;
            
            // Re-render footer/bottom pane so they stay at the bottom of the scrollback
            console.Write(_bottomPane.Render(state));
            console.Write(Environment.NewLine);
            console.Write(_footerRenderer.Render(state));
        }
    }
}
