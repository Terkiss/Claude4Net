using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace Claude4Net.Tests;

public class K053aLumenFidelityTests
{
    [Fact]
    public void MarkupCell_GreenText_RendersAnsiNotRawMarkup()
    {
        // Setup
        var cell = new MarkupCell("[green]Hello[/]");
        int width = 80;

        // Execute
        var lines = HistoryCellRenderer.Render(cell, width).ToList();

        // Verify
        Assert.NotEmpty(lines);
        string output = lines[0].Text;

        // Should NOT contain the literal markup tag
        Assert.DoesNotContain("[green]", output);
        // Should contain ANSI escape code for green (32 or 38;5;...)
        Assert.Contains("\x1b[", output);
        Assert.Contains("Hello", output);
    }

    [Fact]
    public void CommandOutput_KoreanText_DoesNotBecomeQuestionMarks()
    {
        // P1-2: Use Unicode escape for safety
        // \uC548\uB155\uD558\uC138\uC694
        string koreanText = "\uC548\uB155\uD558\uC138\uC694";
        var cell = new MarkupCell(koreanText);
        int width = 80;

        var lines = HistoryCellRenderer.Render(cell, width).ToList();

        Assert.NotEmpty(lines);
        // If it was corrupted to ????, this would fail
        Assert.Equal(koreanText, TerminalText.StripAnsi(lines[0].Text));
    }

    [Fact]
    public void Footer_Render_DoesNotDropFirstCharacter()
    {
        // P1-3: Real verification of footer repaint
        using var sw = new System.IO.StringWriter();
        var renderer = new LumenTerminalRenderer(sw)
        {
            IsRedirected = false // Force ANSI output for testing
        };

        // Frame 1: Initial render
        var frame1 = new LumenFrame(
            new List<DisplayLine> { new DisplayLine("Line 1", DisplayLineKind.Transcript) },
            new CursorPosition(0, 0, true),
            80, 1
        );
        renderer.Render(frame1);
        sw.GetStringBuilder().Clear();

        // Frame 2: Repaint (same height)
        var frame2 = new LumenFrame(
            new List<DisplayLine> { new DisplayLine("Footer Content", DisplayLineKind.Transcript) },
            new CursorPosition(0, 0, true),
            80, 1
        );
        renderer.Render(frame2);

        string output = sw.ToString();

        // The output should contain "Footer Content" exactly.
        Assert.Contains("Footer Content", output);

        // Ensure \r was used to return to start of line before clearing
        Assert.Contains("\r\x1b[2K", output);
    }

    [Fact]
    public void ToolResult_Render_EscapesUntrustedMarkup()
    {
        string untrusted = "[bold]This should be escaped[/]";
        var cell = new ToolResultCell("id", untrusted);
        int width = 80;

        var lines = HistoryCellRenderer.Render(cell, width).ToList();

        // Join all lines and strip ANSI to check content
        string plainText = string.Join("", lines.Select(l => TerminalText.StripAnsi(l.Text)));

        // The literal markup should be preserved because it was escaped for Spectre
        Assert.Contains("[bold]This should be escaped[/]", plainText);
    }

    [Fact]
    public void PlainText_Render_PreservesKoreanAndCjk()
    {
        // \uD55C\uAE00 (Korean)
        string text = "\uD55C\uAE00 Wide Text";
        int width = 10; // width: 4 + 1 + 5 = 10

        var lines = TerminalText.WrapByDisplayWidth(text, width);

        Assert.Equal(2, lines.Count);
        // Line 1: 4 + 1 + 5 = 10 width
        Assert.Equal("\uD55C\uAE00 Wide ", lines[0]);
        Assert.Equal("Text", lines[1]);
    }

    [Fact]
    public void TerminalText_DisplayWidth_IgnoresAnsi()
    {
        string ansiText = "\x1b[31mRed\x1b[0mText";
        int width = TerminalText.DisplayWidth(ansiText);

        // "Red" (3) + "Text" (4) = 7
        Assert.Equal(7, width);
    }

    [Fact]
    public void TerminalText_Wrap_PreservesAnsi()
    {
        string ansiText = "\x1b[31mRed\x1b[0m Long Text";
        int width = 8;

        var lines = TerminalText.WrapByDisplayWidth(ansiText, width);

        // "\x1b[31mRed\x1b[0m Long" -> width 3 + 1 + 4 = 8.
        Assert.Equal(2, lines.Count);
        Assert.StartsWith("\x1b[31m", lines[0]);
        Assert.Contains("Red", lines[0]);
        Assert.EndsWith("Long", lines[0]);
        Assert.Equal(" Text", lines[1]);
    }

    [Fact]
    public void TerminalText_TruncateByDisplayWidth_AnsiAware()
    {
        // P2-2: Truncate tests
        // \uD55C\uAE00 is 4 width.
        string text = "\x1b[31m\uD55C\uAE00\x1b[0m Text"; // 4 + 1 + 4 = 9 width

        // Truncate to 7 width. Suffix "..." is 3. Available = 4.
        // \uD55C\uAE00 fits in 4.
        string result = TerminalText.TruncateByDisplayWidth(text, 7, "...");

        Assert.StartsWith("\x1b[31m", result);
        Assert.Contains("\uD55C\uAE00", result);
        Assert.EndsWith("...", result);
        Assert.Equal(7, TerminalText.DisplayWidth(result));
    }

    [Fact]
    public void TerminalText_Truncate_DoesNotBreakAnsiSequence()
    {
        // If we truncate in the middle of a sequence, it's bad.
        // But our logic processes ANSI runes as atomic units.
        string text = "ABC\x1b[31mDEF GHI JKL MNO";
        // ABC (3) + D (1) = 4. 4 + "..." (3) = 7.
        string result = TerminalText.TruncateByDisplayWidth(text, 7, "...");

        Assert.Equal("ABC\x1b[31mD...", result);
    }
}
