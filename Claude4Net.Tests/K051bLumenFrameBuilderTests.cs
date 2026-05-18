using Xunit;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using System.Collections.Generic;
using System.Linq;
using Spectre.Console.Rendering;
using System;

namespace Claude4Net.Tests;

public class K051bLumenFrameBuilderTests
{
    private readonly LumenFrameBuilder _builder = new();

    private class TestCell(string text) : HistoryCell
    {
        public override string ToPlainText() => text;
        public override IRenderable GetRenderable() => throw new NotImplementedException();
    }

    [Fact]
    public void Build_CreatesFixedTranscriptInputFooterRegions()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 24, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        Assert.Equal(24, frame.Lines.Count);
        Assert.Equal(DisplayLineKind.Footer, frame.Lines.Last().Kind);
        Assert.Equal(DisplayLineKind.Input, frame.Lines[frame.Lines.Count - 2].Kind);
        Assert.All(frame.Lines.Take(22), l => Assert.Equal(DisplayLineKind.Transcript, l.Kind));
    }

    [Fact]
    public void Build_DoesNotPutFooterIntoTranscript()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var transcriptLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Transcript);
        Assert.DoesNotContain(transcriptLines, l => l.Text.Contains("IDLE") || l.Text.Contains("RUNNING"));
    }

    [Fact]
    public void Build_DoesNotPutInputPlaceholderIntoTranscript()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, "my input", 0);

        var transcriptLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Transcript);
        Assert.DoesNotContain(transcriptLines, l => l.Text.Contains("my input"));
    }

    [Fact]
    public void Build_ShowsCurrentInputBuffer()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, "hello world", 0);

        var inputLine = frame.Lines.First(l => l.Kind == DisplayLineKind.Input);
        Assert.Contains("hello world", inputLine.Text);
    }

    [Fact]
    public void Build_UsesTailOfTranscriptWhenHistoryExceedsHeight()
    {
        var state = new LumenState
        {
            History = Enumerable.Range(1, 100).Select(i => (HistoryCell)new TestCell($"Line {i}")).ToList()
        };
        var metrics = new TerminalMetrics(80, 10, true, false); // 8 transcript lines

        var frame = _builder.Build(state, metrics, "", 0);

        var transcriptLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Transcript).ToList();
        Assert.Equal(8, transcriptLines.Count);
        Assert.Equal("Line 100", transcriptLines.Last().Text);
        Assert.Equal("Line 93", transcriptLines.First().Text);
    }

    [Fact]
    public void Build_UsesCompactFooterAt80Columns()
    {
        var state = new LumenState { Provider = "Anthropic", Model = "claude-3-sonnet" };
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var footer = frame.Lines.Last().Text;
        Assert.Contains("P: Anthropic", footer);
        Assert.Contains("M: claude-3-sonnet", footer);
        Assert.DoesNotContain("Provider: Anthropic", footer);
    }

    [Fact]
    public void Build_UsesMinimalFooterAtVeryNarrowWidth()
    {
        var state = new LumenState { IsRunning = true };
        var metrics = new TerminalMetrics(10, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var footer = frame.Lines.Last().Text;
        Assert.StartsWith("[RUNNING]", footer);
    }

    [Fact]
    public void Build_WrapsKoreanTextByDisplayWidth()
    {
        var state = new LumenState
        {
            History = new List<HistoryCell> { new TestCell("안녕하세요") }
        };
        var metrics = new TerminalMetrics(5, 10, true, false); // 5 width

        var frame = _builder.Build(state, metrics, "", 0);

        var transcriptLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Transcript && !string.IsNullOrEmpty(l.Text)).ToList();
        Assert.Equal(3, transcriptLines.Count);
        Assert.Equal("안녕", transcriptLines[0].Text);
        Assert.Equal("하세", transcriptLines[1].Text);
        Assert.Equal("요", transcriptLines[2].Text);
    }

    [Fact]
    public void Build_DoesNotSplitSurrogatePairs()
    {
        var state = new LumenState
        {
            History = new List<HistoryCell> { new TestCell("A💡B") }
        };
        var metrics = new TerminalMetrics(2, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var transcriptLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Transcript && !string.IsNullOrEmpty(l.Text)).ToList();
        Assert.Equal(2, transcriptLines.Count);
    }

    [Fact]
    public void Build_ReturnsCursorInsideInputPane()
    {
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 24, true, false);

        var frame = _builder.Build(state, metrics, "abc", 1); // cursor after 'a'

        Assert.Equal(22, frame.Cursor.Top); // line 22 is input
        Assert.Equal(3, frame.Cursor.Left); // "> a" is 3 chars ('>', ' ', 'a')
    }

    [Fact]
    public void Build_FooterHandlesLongModelNameWithoutOverflow()
    {
        var longModel = new string('x', 100);
        var state = new LumenState { Provider = "P", Model = longModel };
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var footer = frame.Lines.Last().Text;
        Assert.Equal(80, TerminalText.DisplayWidth(footer));
    }

    [Fact]
    public void Build_FooterHandlesCjkContentWithoutOverflow()
    {
        var cjkProvider = "안녕히계세요주인님사랑해요"; // 26 display width
        var state = new LumenState { Provider = cjkProvider, Model = "M" };
        var metrics = new TerminalMetrics(40, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var footer = frame.Lines.Last().Text;
        Assert.Equal(40, TerminalText.DisplayWidth(footer));
    }

    [Fact]
    public void Build_FooterAtNarrowWidthDoesNotOverflow()
    {
        var state = new LumenState { IsRunning = true };
        var metrics = new TerminalMetrics(5, 10, true, false);

        var frame = _builder.Build(state, metrics, "", 0);

        var footer = frame.Lines.Last().Text;
        Assert.Equal(5, TerminalText.DisplayWidth(footer));
        Assert.Contains("[RUN", footer);
    }

    [Fact]
    public void Build_LongInput_DoesNotExceedFrameHeight()
    {
        var longInput = new string('a', 500);
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, longInput, 0);

        Assert.Equal(10, frame.Lines.Count);
    }

    [Fact]
    public void Build_LongInput_ShowsTailWithinInputPane()
    {
        var longInput = new string('a', 50);
        var state = new LumenState();
        var metrics = new TerminalMetrics(10, 10, true, false);

        var frame = _builder.Build(state, metrics, longInput, 50); // Cursor at end

        var inputLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Input).ToList();
        Assert.Equal(4, inputLines.Count);
        Assert.Equal("aa", inputLines.Last().Text);
    }

    [Fact]
    public void Build_LongInput_CursorRemainsInsideFrame()
    {
        var longInput = new string('a', 500);
        var state = new LumenState();
        var metrics = new TerminalMetrics(80, 10, true, false);

        var frame = _builder.Build(state, metrics, longInput, longInput.Length);

        Assert.True(frame.Cursor.Top >= 0 && frame.Cursor.Top < metrics.Height);
        Assert.True(frame.Cursor.Left >= 0 && frame.Cursor.Left < metrics.Width);
    }
}
