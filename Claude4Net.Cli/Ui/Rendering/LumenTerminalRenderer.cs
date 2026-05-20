using System;
using System.Collections.Generic;
using System.Text;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Interface for low-level terminal frame rendering.
/// </summary>
public interface ILumenTerminalRenderer
{
    /// <summary>
    /// Renders a frame to the terminal.
    /// </summary>
    void Render(LumenFrame frame);

    /// <summary>
    /// Prepares the terminal for rendering (e.g., hiding cursor).
    /// </summary>
    void Setup();

    /// <summary>
    /// Restores terminal state (e.g., showing cursor).
    /// </summary>
    void Cleanup();
}

/// <summary>
/// ANSI-based terminal renderer for Lumen frames.
/// </summary>
public sealed class LumenTerminalRenderer(System.IO.TextWriter? writer = null) : ILumenTerminalRenderer
{
    private readonly System.IO.TextWriter _writer = writer ?? Console.Out;
    private bool _isSetup = false;
    private int _lastLineCount = 0;

    /// <summary>
    /// Gets or sets whether the output is redirected, disabling ANSI control sequences.
    /// </summary>
    public bool IsRedirected { get; set; } = Console.IsOutputRedirected;

    public void Setup()
    {
        if (_isSetup || IsRedirected) return;

        // Initial setup for the terminal session
        _writer.Write("\x1b[?25l"); // Hide cursor as baseline
        _isSetup = true;
    }

    public void Cleanup()
    {
        if (!_isSetup || IsRedirected) return;
        // Restore terminal state at the end of session
        _writer.Write("\x1b[?25h"); // Ensure cursor is visible
        _isSetup = false;
    }

    public void Render(LumenFrame frame)
    {
        if (IsRedirected)
        {
            RenderFallback(frame);
            return;
        }

        Setup();

        // 0. Hide cursor at the start of every render to prevent flickering during repaint
        _writer.Write("\x1b[?25l");

        try
        {
            // 1. Move cursor back to the start of the UI region
            if (_lastLineCount > 1)
            {
                // Move up (_lastLineCount - 1) lines and return to start of line
                // We move up N-1 because the cursor is already on the last line we printed
                _writer.Write($"\x1b[{_lastLineCount - 1}A\r");
            }
            else if (_lastLineCount == 1)
            {
                _writer.Write("\r");
            }

            // 2. Render all lines
            var sb = new StringBuilder();
            for (int i = 0; i < frame.Lines.Count; i++)
            {
                // \x1b[2K: Clear entire line, \r: move to start of line
                sb.Append("\r\x1b[2K");
                string text = frame.Lines[i].Text;
                if (i == frame.Lines.Count - 1 && frame.Width > 0)
                {
                    int displayWidth = TerminalText.DisplayWidth(text);
                    if (displayWidth >= frame.Width)
                    {
                        text = TerminalText.TruncateByDisplayWidth(text, frame.Width - 1, "");
                    }
                }
                sb.Append(text);

                // Only add newline if it's not the last line to prevent terminal scrolling
                if (i < frame.Lines.Count - 1)
                {
                    sb.Append("\r\n");
                }
            }

            _writer.Write(sb.ToString());
            _lastLineCount = frame.Lines.Count;

            // 3. Position Cursor
            int moveUp = _lastLineCount - 1 - frame.Cursor.Top;
            if (moveUp > 0)
            {
                _writer.Write($"\x1b[{moveUp}A");
            }
            else if (moveUp < 0)
            {
                _writer.Write($"\x1b[{Math.Abs(moveUp)}B");
            }

            if (frame.Cursor.Left > 0)
            {
                _writer.Write($"\x1b[{frame.Cursor.Left + 1}G");
            }
            else
            {
                _writer.Write("\r");
            }
        }
        finally
        {
            // 4. Restore Cursor Visibility based on frame state
            if (frame.Cursor.Visible)
            {
                _writer.Write("\x1b[?25h");
            }
            else
            {
                _writer.Write("\x1b[?25l");
            }
        }
    }

    private void RenderFallback(LumenFrame frame)
    {
        // For redirected output, just print the lines once.
        // We don't try to overwrite previous frames.
        var sb = new StringBuilder();
        foreach (var line in frame.Lines)
        {
            sb.AppendLine(line.Text);
        }
        _writer.Write(sb.ToString());
    }
}
