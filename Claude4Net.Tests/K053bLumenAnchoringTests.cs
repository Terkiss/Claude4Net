using Xunit;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.Cli.Ui.Events;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace Claude4Net.Tests;

public class K053bLumenAnchoringTests
{
    private readonly LumenFrameBuilder _builder = new();

    [Fact]
    public void FrameBuilder_LastLine_IsAlwaysFooter()
    {
        var state = new LumenState();

        // Test various terminal heights
        var heights = new[] { 1, 2, 3, 5, 20 };
        foreach (var height in heights)
        {
            var metrics = new TerminalMetrics(80, height, true, false);
            var frame = _builder.Build(state, metrics, "test input", 0);

            Assert.Equal(height, frame.Lines.Count);
            Assert.Equal(DisplayLineKind.Footer, frame.Lines.Last().Kind);
        }
    }

    [Fact]
    public void FrameBuilder_InputLine_IsAlwaysAboveFooter()
    {
        var state = new LumenState();

        // Test heights where input can be allocated (height >= 2)
        var heights = new[] { 2, 3, 5, 20 };
        foreach (var height in heights)
        {
            var metrics = new TerminalMetrics(80, height, true, false);
            var frame = _builder.Build(state, metrics, "test input", 0);

            Assert.Equal(height, frame.Lines.Count);
            Assert.Equal(DisplayLineKind.Input, frame.Lines[height - 2].Kind);
        }
    }

    [Fact]
    public void TerminalRenderer_DoesNotDropFooterFirstCharacter_AfterAssistantOutput()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        var frame = new LumenFrame(
            new List<DisplayLine> {
                new DisplayLine("Assistant Output", DisplayLineKind.Transcript),
                new DisplayLine("> input", DisplayLineKind.Input),
                new DisplayLine(" IDLE | Provider", DisplayLineKind.Footer)
            },
            new CursorPosition(7, 1, true),
            80, 3
        );

        // Render initially
        renderer.Render(frame);
        var initialOutput = sb.ToString();
        Assert.Contains(" IDLE | Provider", initialOutput);

        sb.Clear();

        // Render again to simulate repaint
        renderer.Render(frame);
        var repaintOutput = sb.ToString();

        // Ensure the repaint does not emit invalid escape sequence \x1b[0A which might swallow text,
        // and ensure the footer content starts with the original space and 'I'
        Assert.DoesNotContain("\x1b[0A", repaintOutput);
        Assert.Contains(" IDLE | Provider", repaintOutput);
    }

    [Fact]
    public void TerminalRenderer_CursorStaysInsideInputRegion()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        // Build with multiline input to check clamping
        var frame = _builder.Build(state, metrics, "line1\nline2\nline3\nline4\nline5\nline6", 15);

        // Height is 10.
        // Footer: 1 line
        // Input max height allowed: Math.Min(4, 10 - 2) = 4 lines.
        // Transcript: 10 - 1 - 4 = 5 lines.
        // CursorTop should be clamped between 5 (transcriptHeight) and 8 (5 + 4 - 1).
        Assert.True(frame.Cursor.Top >= 5 && frame.Cursor.Top <= 8,
            $"Cursor.Top ({frame.Cursor.Top}) must be within input range [5, 8]");
        Assert.Equal(DisplayLineKind.Input, frame.Lines[frame.Cursor.Top].Kind);
    }

    [Fact]
    public void LongAssistantOutput_DoesNotMovePromptBelowFooter()
    {
        var state = new LumenState();

        // Add many lines of assistant output
        for (int i = 0; i < 100; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Assistant line {i}"));
        }

        var metrics = new TerminalMetrics(80, 24, true, false);
        var frame = _builder.Build(state, metrics, "my current prompt", 0);

        Assert.Equal(24, frame.Lines.Count);
        Assert.Equal(DisplayLineKind.Footer, frame.Lines.Last().Kind);
        Assert.Equal(DisplayLineKind.Input, frame.Lines[^2].Kind);
        Assert.Contains("my current prompt", frame.Lines[^2].Text);
    }

    [Fact]
    public void EmptyInput_PlaceholderDoesNotEnterTranscript()
    {
        var state = new LumenState();
        Assert.Empty(state.History);

        // Submit empty input
        state = LumenReducer.Reduce(state, new UserPromptSubmittedEvent(""));
        Assert.Empty(state.History);

        // Submit whitespace input
        state = LumenReducer.Reduce(state, new UserPromptSubmittedEvent("   "));
        Assert.Empty(state.History);

        // Submit valid input
        state = LumenReducer.Reduce(state, new UserPromptSubmittedEvent("valid query"));
        Assert.Single(state.History);
        Assert.IsType<UserPromptCell>(state.History.First());
    }
}
