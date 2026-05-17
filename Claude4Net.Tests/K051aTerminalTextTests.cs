using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.Cli.Ui.Rendering;
using Xunit;

namespace Claude4Net.Tests;

public class K051aTerminalTextTests
{
    private const string KoreanHello = "\uC548\uB155\uD558\uC138\uC694"; // Korean "hello"
    private const string CjkSample = "\u4E2D\u6587"; // Chinese sample
    private const string FullWidthPunctuation = "\uFF01\uFF1F"; // full-width exclamation/question
    private const string Emoji = "\U0001F600"; // grinning face emoji
    private const string CombiningAcute = "a\u0301"; // a + combining acute accent

    [Fact]
    public void DisplayWidth_Ascii_EqualsLength()
    {
        string text = "Hello, World!";
        Assert.Equal(text.Length, TerminalText.DisplayWidth(text));
    }

    [Fact]
    public void DisplayWidth_Hangul_IsDoubleWidth()
    {
        Assert.Equal(10, TerminalText.DisplayWidth(KoreanHello));
    }

    [Fact]
    public void DisplayWidth_CjkIdeograph_IsDoubleWidth()
    {
        Assert.Equal(4, TerminalText.DisplayWidth(CjkSample));
    }

    [Fact]
    public void DisplayWidth_FullWidthPunctuation_IsDoubleWidth()
    {
        Assert.Equal(4, TerminalText.DisplayWidth(FullWidthPunctuation));
    }

    [Fact]
    public void DisplayWidth_CombiningMarks_AreZeroWidth()
    {
        Assert.Equal(1, TerminalText.DisplayWidth(CombiningAcute));
    }

    [Fact]
    public void DisplayWidth_Emoji_HandledSafely()
    {
        int width = TerminalText.DisplayWidth(Emoji);
        Assert.True(width > 0);
    }

    [Fact]
    public void DisplayWidth_EmptyAndNull_AreZero()
    {
        Assert.Equal(0, TerminalText.DisplayWidth(""));
        Assert.Equal(0, TerminalText.DisplayWidth(null));
    }

    [Fact]
    public void Wrap_RespectsDisplayWidth()
    {
        var lines = TerminalText.WrapByDisplayWidth(KoreanHello, 4);

        Assert.Equal(3, lines.Count);
        Assert.Equal("\uC548\uB155", lines[0]);
        Assert.Equal("\uD558\uC138", lines[1]);
        Assert.Equal("\uC694", lines[2]);
    }

    [Fact]
    public void Wrap_WideRuneWiderThanWidth_DoesNotEmitLeadingEmptyLine()
    {
        string text = "\uC548";
        var lines = TerminalText.WrapByDisplayWidth(text, 1);

        Assert.Single(lines);
        Assert.NotEmpty(lines[0]);
        Assert.Equal(text, lines[0]);
    }

    [Fact]
    public void Wrap_DoesNotSplitInsideSurrogatePairs()
    {
        string text = "A\u4E2DB"; // A(1) CJK(2) B(1) = 4 width
        var lines = TerminalText.WrapByDisplayWidth(text, 2);

        Assert.Equal(3, lines.Count);
        Assert.Equal("A", lines[0]);
        Assert.Equal("\u4E2D", lines[1]);
        Assert.Equal("B", lines[2]);
    }

    [Fact]
    public void Truncate_AppendsSuffixWithinDisplayWidth()
    {
        string truncated = TerminalText.TruncateByDisplayWidth(KoreanHello, 7, "...");
        Assert.Equal("\uC548\uB155...", truncated);
        Assert.True(TerminalText.DisplayWidth(truncated) <= 7);
    }

    [Fact]
    public void Truncate_DoesNotSplitSurrogatePairs()
    {
        string text = "\uD83D\uDE00A";
        string truncated = TerminalText.TruncateByDisplayWidth(text, 1, ".");
        Assert.Equal(".", truncated);
    }

    [Fact]
    public void LumenFrame_ConstructionWorks()
    {
        var lines = new List<DisplayLine>
        {
            new DisplayLine("Line 1", DisplayLineKind.Transcript),
            new DisplayLine("----", DisplayLineKind.Separator)
        };
        var cursor = new CursorPosition(0, 0, true);
        var frame = new LumenFrame(lines, cursor, 80, 24);

        Assert.Equal(2, frame.Lines.Count);
        Assert.Equal(80, frame.Width);
    }

    [Fact]
    public void FooterState_And_RunStatusState_ConstructionWorks()
    {
        var runStatus = new RunStatusState(LumenRunPhase.Thinking, "Thinking...", "FileWrite", 1);
        var footer = new FooterState("IDLE", "gemini", "flash", "ReadWrite", "sid", "Hint", "Notice");

        Assert.Equal(LumenRunPhase.Thinking, runStatus.Phase);
        Assert.Equal("gemini", footer.Provider);
    }

    [Fact]
    public void TerminalMetrics_ConstructionWorks()
    {
        var metrics = new TerminalMetrics(100, 30, true, false);
        Assert.Equal(100, metrics.Width);
        Assert.True(metrics.SupportsAnsi);
    }
}
