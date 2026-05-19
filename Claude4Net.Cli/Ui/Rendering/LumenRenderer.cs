using Spectre.Console;
using Spectre.Console.Rendering;
using Claude4Net.Cli.Ui.Input;
using System.Linq;
using System;

namespace Claude4Net.Cli.Ui.Rendering;

public class LumenRenderer(
    IAnsiConsole console,
    ILumenFrameBuilder? frameBuilder = null,
    ILumenTerminalRenderer? terminalRenderer = null)
{
    private readonly ChatSurface _chatSurface = new();
    private readonly FooterRenderer _footerRenderer = new();
    private readonly BottomPane _bottomPane = new();
    private readonly DialogLayer _dialogLayer = new();

    private readonly ILumenFrameBuilder _frameBuilder = frameBuilder ?? new LumenFrameBuilder();
    private readonly ILumenTerminalRenderer _terminalRenderer = terminalRenderer ?? new LumenTerminalRenderer();

    private int _lastRenderedHistoryCount = 0;
    private PromptComposerState? _lastComposerState;
    private bool _lumenModeEnabled = false;

    public void EnableLumenMode() => _lumenModeEnabled = true;

    public bool IsLumenMode => _lumenModeEnabled;

    /// <summary>
    /// Renders the entire state including history and input area.
    /// </summary>
    public void RenderFull(LumenState state, PromptComposerState composerState)
    {
        if (_lumenModeEnabled)
        {
            RenderLumenFrame(state, composerState);
            return;
        }

        console.Write(_chatSurface.Render(state.History));
        _lastRenderedHistoryCount = state.History.Count;
        RefreshInput(state, composerState);
    }

    /// <summary>
    /// Refreshes the input area (bottom pane and footer).
    /// </summary>
    public void RefreshInput(LumenState state, PromptComposerState composerState)
    {
        if (_lumenModeEnabled)
        {
            RenderLumenFrame(state, composerState);
            return;
        }

        _lastComposerState = composerState;

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
        if (_lumenModeEnabled)
        {
            if (_lastComposerState != null)
            {
                RenderLumenFrame(state, _lastComposerState);
            }
            return;
        }

        if (state.History.Count > _lastRenderedHistoryCount)
        {
            var newCells = state.History.Skip(_lastRenderedHistoryCount).ToList();
            console.Write(_chatSurface.Render(newCells));
            _lastRenderedHistoryCount = state.History.Count;
        }
    }

    private void RenderLumenFrame(LumenState state, PromptComposerState composerState)
    {
        _lastComposerState = composerState;

        int width = 80;
        int height = 24;
        try
        {
            width = Console.WindowWidth;
            height = Console.WindowHeight;
        }
        catch { }

        var metrics = new TerminalMetrics(
            width,
            height,
            true,
            Console.IsOutputRedirected);

        var frame = _frameBuilder.Build(state, metrics, composerState.Text, composerState.CursorPosition);
        _terminalRenderer.Render(frame);
    }

    public void Shutdown()
    {
        if (_lumenModeEnabled)
        {
            _terminalRenderer.Cleanup();
        }
    }
}
