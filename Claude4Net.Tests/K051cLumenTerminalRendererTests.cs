using Xunit;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using System.Collections.Generic;
using System;
using System.IO;
using System.Text;
using Spectre.Console;
using Claude4Net.Cli.Ui.Input;

namespace Claude4Net.Tests;

public class K051cLumenTerminalRendererTests
{
    [Fact]
    public void Setup_HidesCursor_WhenNotRedirected()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Setup();

        Assert.Contains("\x1b[?25l", sb.ToString());
    }

    [Fact]
    public void Cleanup_ShowsCursor_WhenNotRedirected()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Setup();
        sb.Clear();
        renderer.Cleanup();

        Assert.Contains("\x1b[?25h", sb.ToString());
    }

    [Fact]
    public void Render_MovesCursorUp_WhenPreviousLinesExist()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        // First render
        renderer.Render(new LumenFrame(new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24));
        sb.Clear();

        // Second render - should move up 1 line
        renderer.Render(new LumenFrame(new List<DisplayLine> { new DisplayLine("L2", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24));

        Assert.Contains("\r", sb.ToString());
    }

    [Fact]
    public void Render_ClearsLines_BeforeWriting()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Render(new LumenFrame(new List<DisplayLine> { new DisplayLine("TestLine", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24));

        Assert.Contains("\x1b[2KTestLine", sb.ToString());
    }

    [Fact]
    public void Render_PositionsCursorTop_Correctly()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        // 3 lines total, cursor at Top 1 (index 1, middle line)
        // lastLineCount = 3, cursor.Top = 1 -> moveUp = 2
        var frame = new LumenFrame(
            new List<DisplayLine> {
                new DisplayLine("L1", DisplayLineKind.Transcript),
                new DisplayLine("L2", DisplayLineKind.Transcript),
                new DisplayLine("L3", DisplayLineKind.Transcript)
            },
            new CursorPosition(0, 1, true), // Left=0, Top=1
            80, 24);

        renderer.Render(frame);

        Assert.Contains("\x1b[1A", sb.ToString());
    }

    [Fact]
    public void Render_PositionsCursorLeft_Correctly()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        var frame = new LumenFrame(
            new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) },
            new CursorPosition(5, 0, true), // Left=5, Top=0
            80, 24);

        renderer.Render(frame);

        // Horizontal position is 1-based in ANSI: 5 + 1 = 6
        Assert.Contains("\x1b[6G", sb.ToString());
    }

    [Fact]
    public void Render_RespectsCursorVisibility_Visible()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        var frame = new LumenFrame(
            new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) },
            new CursorPosition(0, 0, true),
            80, 24);

        renderer.Render(frame);

        Assert.Contains("\x1b[?25h", sb.ToString());
    }

    [Fact]
    public void Render_RespectsCursorVisibility_Hidden()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        var frame = new LumenFrame(
            new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) },
            new CursorPosition(0, 0, false),
            80, 24);

        renderer.Render(frame);

        Assert.Contains("\x1b[?25l", sb.ToString());
    }

    [Fact]
    public void Render_HandlesEmptyFrame()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Render(new LumenFrame(new List<DisplayLine>(), new CursorPosition(0, 0, true), 80, 24));

        Assert.DoesNotContain("\x1b[1A", sb.ToString()); // No move up if nothing was rendered before
        Assert.Contains("\r", sb.ToString()); // Should still reset horizontal
    }

    [Fact]
    public void Render_FallbackMode_NoAnsi_WhenRedirected()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = true };

        var frame = new LumenFrame(
            new List<DisplayLine> {
                new DisplayLine("L1", DisplayLineKind.Transcript),
                new DisplayLine("L2", DisplayLineKind.Transcript)
            },
            new CursorPosition(0, 0, true),
            80, 24);

        renderer.Render(frame);

        string output = sb.ToString();
        // Check for absence of escape character (\x1b)
        Assert.False(output.Contains("\x1b"), "Output should not contain ANSI escape sequences in redirected mode.");
        Assert.Contains("L1", output);
        Assert.Contains("L2", output);
    }

    [Fact]
    public void Setup_IsIdempotent()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Setup();
        string first = sb.ToString();
        sb.Clear();
        renderer.Setup();

        Assert.Empty(sb.ToString());
    }

    [Fact]
    public void Cleanup_IsSafe_WithoutSetup()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Cleanup();

        Assert.Empty(sb.ToString());
    }

    [Fact]
    public void Render_UpdatesLastLineCount()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        renderer.Render(new LumenFrame(new List<DisplayLine> {
            new DisplayLine("L1", DisplayLineKind.Transcript),
            new DisplayLine("L2", DisplayLineKind.Transcript)
        }, new CursorPosition(0, 0, true), 80, 24));
        sb.Clear();

        // If lastLineCount was updated to 2, next render should move up 2
        renderer.Render(new LumenFrame(new List<DisplayLine> {
            new DisplayLine("L3", DisplayLineKind.Transcript)
        }, new CursorPosition(0, 0, true), 80, 24));

        Assert.Contains("\x1b[1A\r", sb.ToString());
    }

    [Fact]
    public void Render_HidesCursorBeforeEveryRepaint_WhenCursorWasVisibleAfterPreviousRender()
    {
        var sb = new StringBuilder();
        using var writer = new StringWriter(sb);
        var renderer = new LumenTerminalRenderer(writer) { IsRedirected = false };

        // Render 1: Cursor visible at end
        renderer.Render(new LumenFrame(new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24));

        // Output should end with show cursor
        Assert.EndsWith("\x1b[?25h", sb.ToString());
        sb.Clear();

        // Render 2: Should start by hiding cursor again
        renderer.Render(new LumenFrame(new List<DisplayLine> { new DisplayLine("L2", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24));

        Assert.StartsWith("\x1b[?25l", sb.ToString());
    }

    [Fact]
    public void Render_RestoresCursor_WhenWriterThrowsDuringRepaint()
    {
        var mockWriter = new ThrowingWriter();
        var renderer = new LumenTerminalRenderer(mockWriter) { IsRedirected = false };

        var frame = new LumenFrame(new List<DisplayLine> { new DisplayLine("L1", DisplayLineKind.Transcript) }, new CursorPosition(0, 0, true), 80, 24);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => renderer.Render(frame));

        // Verify finally block was executed - should contain show cursor at the end
        Assert.Contains("\x1b[?25h", mockWriter.Log);
    }

    private class ThrowingWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public string Log = "";
        private int _writeCount = 0;

        public override void Write(string? value)
        {
            if (value == null) return;
            Log += value;
            _writeCount++;

            // Throw after initial cursor hide and move up
            if (_writeCount > 2) throw new InvalidOperationException("Simulated failure during repaint");
        }
    }

    [Fact]
    public void LumenRenderer_InLumenMode_DelegatesAllCallsToTerminalRenderer()
    {
        var console = new Spectre.Console.Testing.TestConsole();
        var mockTerm = new MockTerminalRenderer();
        var renderer = new LumenRenderer(console, null, mockTerm);
        renderer.EnableLumenMode();

        var state = new LumenState();
        var composerState = new PromptComposerState("input", 5, null);

        // 1. RenderFull
        renderer.RenderFull(state, composerState);
        Assert.Equal(1, mockTerm.RenderCount);

        // 2. RefreshInput
        renderer.RefreshInput(state, composerState);
        Assert.Equal(2, mockTerm.RenderCount);

        // 3. RenderAppend (background output) - should use last composer state
        renderer.RenderAppend(state);
        Assert.Equal(3, mockTerm.RenderCount);

        // 4. Shutdown
        renderer.Shutdown();
        Assert.True(mockTerm.IsCleanedUp);
    }

    private class MockTerminalRenderer : ILumenTerminalRenderer
    {
        public int RenderCount { get; private set; }
        public bool IsCleanedUp { get; private set; }
        public void Render(LumenFrame frame) => RenderCount++;
        public void Setup() { }
        public void Cleanup() => IsCleanedUp = true;
    }
}
