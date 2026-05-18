using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Interface for building pure UI frames.
/// </summary>
public interface ILumenFrameBuilder
{
    LumenFrame Build(
        LumenState state,
        TerminalMetrics metrics,
        string currentInput,
        int cursorOffset);
}

/// <summary>
/// Pure implementation of the Lumen UI layout.
/// </summary>
public sealed class LumenFrameBuilder : ILumenFrameBuilder
{
    public LumenFrame Build(LumenState state, TerminalMetrics metrics, string currentInput, int cursorOffset)
    {
        int width = metrics.Width;
        int height = metrics.Height;

        if (width <= 0 || height <= 0)
        {
            return new LumenFrame(new List<DisplayLine>(), new CursorPosition(0, 0, false), width, height);
        }

        // 1. Wrap Input
        string prompt = "> ";
        string fullInputText = prompt + (currentInput ?? string.Empty);
        var allWrappedInput = TerminalText.WrapByDisplayWidth(fullInputText, width);
        if (allWrappedInput.Count == 0) allWrappedInput = new List<string> { prompt };

        // 2. Bounded Input Pane Height
        int maxInputAllowed = Math.Min(4, Math.Max(1, height - 1));
        int inputHeight = Math.Min(allWrappedInput.Count, maxInputAllowed);

        // 3. Transcript Viewport Height
        int transcriptHeight = height - 1 - inputHeight;
        if (transcriptHeight < 0) transcriptHeight = 0;

        // 4. Calculate Cursor and Input Viewport
        int absCursorLeft;
        int absCursorLineOffset;
        CalculateCursor(fullInputText, Math.Min(cursorOffset + prompt.Length, fullInputText.Length), width, out absCursorLeft, out absCursorLineOffset);

        int inputStartLine = Math.Max(0, allWrappedInput.Count - inputHeight);
        if (absCursorLineOffset < inputStartLine)
        {
            inputStartLine = absCursorLineOffset;
        }
        else if (absCursorLineOffset >= inputStartLine + inputHeight)
        {
            inputStartLine = absCursorLineOffset - inputHeight + 1;
        }

        var visibleInputLines = allWrappedInput.Skip(inputStartLine).Take(inputHeight)
            .Select(l => new DisplayLine(l, DisplayLineKind.Input)).ToList();

        // 5. Render Transcript
        var allTranscriptLines = new List<DisplayLine>();
        foreach (var cell in state.History)
        {
            allTranscriptLines.AddRange(HistoryCellRenderer.Render(cell, width));
        }
        var transcriptTail = allTranscriptLines.TakeLast(transcriptHeight).ToList();

        // 6. Assemble lines
        var lines = new List<DisplayLine>();
        int padding = transcriptHeight - transcriptTail.Count;
        for (int i = 0; i < padding; i++)
        {
            lines.Add(new DisplayLine(string.Empty, DisplayLineKind.Transcript));
        }

        lines.AddRange(transcriptTail);
        lines.AddRange(visibleInputLines);

        string footerText = BuildFooterText(state, width);
        lines.Add(new DisplayLine(footerText, DisplayLineKind.Footer));

        // 7. Final Cursor
        int cursorTop = transcriptHeight + (absCursorLineOffset - inputStartLine);
        int cursorLeft = absCursorLeft;

        return new LumenFrame(lines, new CursorPosition(cursorLeft, cursorTop, true), width, height);
    }

    private string BuildFooterText(LumenState state, int width)
    {
        var provider = state.Provider ?? "None";
        var model = state.Model ?? "None";
        var sessionId = state.SessionId ?? "None";
        var status = state.IsRunning ? "RUNNING" : "IDLE";

        string text;
        if (width < 40) // Minimal
        {
            text = $"[{status}]";
        }
        else if (width <= 80) // Compact
        {
            text = $" {status} | P: {provider} | M: {model} ";
        }
        else // Standard
        {
            text = $" {status} | Provider: {provider} | Model: {model} | Session: {sessionId} ";
        }

        int currentWidth = TerminalText.DisplayWidth(text);
        if (currentWidth > width)
        {
            text = TerminalText.TruncateByDisplayWidth(text, width, "");
            currentWidth = TerminalText.DisplayWidth(text);
        }

        return text + new string(' ', Math.Max(0, width - currentWidth));
    }

    private void CalculateCursor(string text, int charOffset, int width, out int left, out int lineOffset)
    {
        int currentLineWidth = 0;
        lineOffset = 0;

        int charIndex = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (charIndex >= charOffset) break;

            int runeWidth = TerminalText.DisplayWidth(rune.ToString());

            if (currentLineWidth > 0 && currentLineWidth + runeWidth > width)
            {
                lineOffset++;
                currentLineWidth = 0;
            }

            currentLineWidth += runeWidth;
            charIndex += rune.Utf16SequenceLength;
        }

        left = currentLineWidth;
        if (left >= width && width > 0)
        {
            left = 0;
            lineOffset++;
        }
    }
}
