using Xunit;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using System.Collections.Generic;
using System.Linq;

namespace Claude4Net.Tests;

public class K052LumenScrollNavigationTests
{
    private readonly LumenFrameBuilder _builder = new();
    private readonly TerminalMetrics _metrics = new(80, 20, true, false);

    [Fact]
    public void FrameBuilder_ManualScroll_KeepsInputAndFooterFixed()
    {
        // Setup state with enough history to scroll
        var state = new LumenState();
        for (int i = 0; i < 50; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i}"));
        }

        // Pinned to bottom
        var framePinned = _builder.Build(state, _metrics, "input", 5);

        // Manual scroll up
        var stateScrolled = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(10));
        var frameScrolled = _builder.Build(stateScrolled, _metrics, "input", 5);

        // Input and footer should be same
        Assert.Equal(framePinned.Lines.Last(), frameScrolled.Lines.Last()); // Footer
        Assert.Equal(framePinned.Lines[^2], frameScrolled.Lines[^2]); // Input line

        // Transcript should differ
        Assert.NotEqual(framePinned.Lines[0].Text, frameScrolled.Lines[0].Text);
    }

    [Fact]
    public void FrameBuilder_PageUp_ShowsOlderTranscriptLines()
    {
        var state = new LumenState();
        for (int i = 0; i < 50; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i:D2}"));
        }

        // Pinned: shows latest (up to Line 49)
        var framePinned = _builder.Build(state, _metrics, "", 0);
        Assert.Contains(framePinned.Lines.Select(l => l.Text), t => t.Contains("Line 49"));

        // Scroll Up 10
        var stateScrolled = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(10));
        var frameScrolled = _builder.Build(stateScrolled, _metrics, "", 0);

        // Should NOT contain Line 49 if scrolled up enough
        Assert.DoesNotContain(frameScrolled.Lines.Take(18).Select(l => l.Text), t => t.Contains("Line 49"));
        Assert.Contains(frameScrolled.Lines.Select(l => l.Text), t => t.Contains("Line 30"));
    }

    [Fact]
    public void FrameBuilder_PageDown_MovesTowardBottom()
    {
        var state = new LumenState();
        for (int i = 0; i < 50; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i:D2}"));
        }

        // Manually set a large offset (but not too large)
        state = state with { Scroll = new ViewportScrollState(30, false) };
        var frameUp = _builder.Build(state, _metrics, "", 0);
        // maxPossibleStart = 50 - 18 = 32. Offset 30 -> startLine = 32 - 30 = 2.
        Assert.Contains(frameUp.Lines.Select(l => l.Text), t => t.Contains("Line 02"));

        // Scroll Down 5 -> Offset 25
        state = LumenReducer.Reduce(state, new ScrollDownRequestedEvent(5));
        var frameDown = _builder.Build(state, _metrics, "", 0);

        // startLine = 32 - 25 = 7.
        Assert.DoesNotContain(frameDown.Lines.Take(18).Select(l => l.Text), t => t.Contains("Line 02"));
        Assert.Contains(frameDown.Lines.Select(l => l.Text), t => t.Contains("Line 07"));
    }

    [Fact]
    public void FrameBuilder_CtrlEnd_RePinsToBottom()
    {
        var state = new LumenState();
        for (int i = 0; i < 50; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i}"));
        }

        // Scrolled
        state = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(10));
        Assert.False(state.Scroll.AutoScroll);

        // Scroll to end
        state = LumenReducer.Reduce(state, new ScrollToEndRequestedEvent());
        Assert.True(state.Scroll.AutoScroll);
        Assert.Equal(0, state.Scroll.ScrollOffset);
    }

    [Fact]
    public void NewOutput_WhenPinnedToBottom_ShowsLatestLines()
    {
        var state = new LumenState();
        for (int i = 0; i < 20; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i}"));
        }

        // Pinned
        Assert.True(state.Scroll.AutoScroll);

        // Add new output
        state = LumenReducer.Reduce(state, new NoticeReceivedEvent("NEW LINE"));

        var frame = _builder.Build(state, _metrics, "", 0);
        Assert.Contains(frame.Lines.Select(l => l.Text), t => t.Contains("NEW LINE"));
        Assert.True(state.Scroll.AutoScroll);
    }

    [Fact]
    public void NewOutput_WhenManualScrolled_PreservesDistanceFromBottom()
    {
        var state = new LumenState();
        for (int i = 0; i < 100; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i:D3}"));
        }

        // Scroll up by 20 lines
        state = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(20));
        var frameBefore = _builder.Build(state, _metrics, "input", 0);

        // New output arrives
        state = LumenReducer.Reduce(state, new NoticeReceivedEvent("HIDDEN NEW LINE"));

        var frameAfter = _builder.Build(state, _metrics, "input", 0);

        // In "distance from bottom" logic, the viewport will jump if ScrollOffset is kept same.
        Assert.False(state.Scroll.AutoScroll);
        Assert.Equal(20, state.Scroll.ScrollOffset);

        // Input and footer should remain fixed
        Assert.Equal(frameBefore.Lines.Last(), frameAfter.Lines.Last());
        Assert.Equal(frameBefore.Lines[^2], frameAfter.Lines[^2]);

        // Verify we are NOT at the bottom
        Assert.DoesNotContain(frameAfter.Lines.Select(l => l.Text), t => t.Contains("HIDDEN NEW LINE"));
    }
    [Fact]
    public void FrameBuilder_HeightInvariant_HoldsWhileScrolled()
    {
        var state = new LumenState();
        for (int i = 0; i < 100; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent($"Line {i}"));
        }

        state = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(50));
        var frame = _builder.Build(state, _metrics, "multiline\ninput\nhere", 0);

        Assert.Equal(_metrics.Height, frame.Lines.Count);
    }

    [Fact]
    public void FrameBuilder_CjkWrappedTranscript_DoesNotBreakViewport()
    {
        var state = new LumenState();
        // Korean text: "안녕하세요" (10 width)
        string cjk = "안녕하세요 안녕하세요 안녕하세요 안녕하세요 안녕하세요"; // 50 width
        for (int i = 0; i < 20; i++)
        {
            state = LumenReducer.Reduce(state, new NoticeReceivedEvent(cjk));
        }

        // metrics width 40 means each line wraps
        var metrics40 = new TerminalMetrics(40, 20, true, false);

        state = LumenReducer.Reduce(state, new ScrollUpRequestedEvent(10));
        var frame = _builder.Build(state, metrics40, "", 0);

        Assert.Equal(20, frame.Lines.Count);
        Assert.All(frame.Lines, l => Assert.True(TerminalText.DisplayWidth(l.Text) <= 40));
    }
}
