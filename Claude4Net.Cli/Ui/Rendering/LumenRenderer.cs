using Spectre.Console;
using Spectre.Console.Rendering;
using Claude4Net.Cli.Ui.Input;

namespace Claude4Net.Cli.Ui.Rendering;

public class LumenRenderer(IAnsiConsole console)
{
    private readonly ChatSurface _chatSurface = new();
    private readonly FooterRenderer _footerRenderer = new();
    private readonly BottomPane _bottomPane = new();
    private readonly DialogLayer _dialogLayer = new();

    private int _lastRenderedHistoryCount = 0;
    private PromptComposerState? _lastComposerState;

    /// <summary>
    /// Renders the entire state including history and input area.
    /// </summary>
    public void RenderFull(LumenState state, PromptComposerState composerState)
    {
        console.Write(_chatSurface.Render(state.History));
        _lastRenderedHistoryCount = state.History.Count;
        RefreshInput(state, composerState);
    }

    /// <summary>
    /// Refreshes the input area (bottom pane and footer).
    /// </summary>
    public void RefreshInput(LumenState state, PromptComposerState composerState)
    {
        _lastComposerState = composerState;

        // P1 Fix: In a more advanced renderer, we would use Live display or cursor management.
        // For now, we append but try to keep it stable.
        if (state.ApprovalDialog.IsVisible)
        {
            var dialog = _dialogLayer.Render(state);
            if (dialog != null) console.Write(dialog);
        }
        else
        {
            console.Write(_bottomPane.Render(state, composerState));
        }

        console.Write(Environment.NewLine);
        console.Write(_footerRenderer.Render(state));
    }

    /// <summary>
    /// Renders only the new cells without refreshing the input area (to prevent transcript accumulation).
    /// </summary>
    public void RenderAppend(LumenState state)
    {
        if (state.History.Count > _lastRenderedHistoryCount)
        {
            var newCells = state.History.Skip(_lastRenderedHistoryCount).ToList();
            console.Write(_chatSurface.Render(newCells));
            _lastRenderedHistoryCount = state.History.Count;
        }
    }
}
