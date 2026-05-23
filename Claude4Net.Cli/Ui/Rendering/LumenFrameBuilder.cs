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

        string prompt = "> ";
        string footerText = BuildFooterText(state, width);
        int absCursorLeft = 0;
        int absCursorLineOffset = 0;

        if (height == 1)
        {
            var singleLine = new List<DisplayLine> { new DisplayLine(footerText, DisplayLineKind.Footer) };
            return new LumenFrame(singleLine, new CursorPosition(0, 0, false), width, height);
        }

        // 1. Wrap Input
        string fullInputText = prompt + (currentInput ?? string.Empty);
        var allWrappedInput = TerminalText.WrapByDisplayWidth(fullInputText, width);
        if (allWrappedInput.Count == 0) allWrappedInput = new List<string> { prompt };

        if (height == 2)
        {
            var twoLines = new List<DisplayLine>
            {
                new DisplayLine(allWrappedInput.Last(), DisplayLineKind.Input),
                new DisplayLine(footerText, DisplayLineKind.Footer)
            };
            CalculateCursor(fullInputText, Math.Min(cursorOffset + prompt.Length, fullInputText.Length), width, out absCursorLeft, out absCursorLineOffset);
            return new LumenFrame(twoLines, new CursorPosition(absCursorLeft, 0, true), width, height);
        }

        // 2. Bounded Input Pane Height (height >= 3)
        int maxInputAllowed = Math.Min(4, Math.Max(1, height - 2));
        int inputHeight = Math.Min(allWrappedInput.Count, maxInputAllowed);

        // Calculate Approval Dialog Height if visible
        bool showDialog = state.ApprovalDialog.IsVisible;
        List<DisplayLine>? dialogLines = null;
        int dialogHeight = 0;

        if (showDialog)
        {
            int upperLimit = Math.Max(0, height - inputHeight - 1);
            int maxAvailableForDialog = upperLimit;
            dialogLines = BuildDialogLines(state.ApprovalDialog, width, maxAvailableForDialog);
            dialogHeight = dialogLines.Count;
        }

        // 3. Transcript Viewport Height
        int transcriptHeight = height - 1 - inputHeight - dialogHeight;
        if (transcriptHeight < 0) transcriptHeight = 0;

        // 4. Calculate Cursor and Input Viewport
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

        // Apply scrolling
        int maxPossibleStart = Math.Max(0, allTranscriptLines.Count - transcriptHeight);
        int startLine;

        if (state.Scroll.AutoScroll)
        {
            startLine = maxPossibleStart;
        }
        else
        {
            // ScrollOffset is distance from bottom
            startLine = maxPossibleStart - state.Scroll.ScrollOffset;
            startLine = Math.Clamp(startLine, 0, maxPossibleStart);
        }

        var visibleTranscript = allTranscriptLines.Skip(startLine).Take(transcriptHeight).ToList();

        // 6. Assemble lines
        var lines = new List<DisplayLine>();
        int padding = transcriptHeight - visibleTranscript.Count;
        for (int i = 0; i < padding; i++)
        {
            lines.Add(new DisplayLine(string.Empty, DisplayLineKind.Transcript));
        }

        lines.AddRange(visibleTranscript);

        if (showDialog && dialogLines != null)
        {
            lines.AddRange(dialogLines);
        }

        lines.AddRange(visibleInputLines);
        lines.Add(new DisplayLine(footerText, DisplayLineKind.Footer));

        // Hard guarantee: total lines must match height exactly under any conditions
        while (lines.Count > height)
        {
            lines.RemoveAt(0);
        }
        while (lines.Count < height)
        {
            lines.Insert(0, new DisplayLine(string.Empty, DisplayLineKind.Transcript));
        }

        if (state.IsCommandPaletteVisible)
        {
            var paletteLines = BuildPaletteLines(state, width);
            int P = paletteLines.Count;
            int idx = lines.Count - (visibleInputLines.Count + 1) - P;
            if (idx >= 0)
            {
                for (int i = 0; i < P; i++)
                {
                    lines[idx + i] = paletteLines[i];
                }
            }
        }

        // 7. Final Cursor
        int cursorTop = transcriptHeight + dialogHeight + (absCursorLineOffset - inputStartLine);
        int cursorMin = transcriptHeight + dialogHeight;
        int cursorMax = transcriptHeight + dialogHeight + inputHeight - 1;
        cursorTop = Math.Clamp(cursorTop, cursorMin, Math.Max(cursorMin, cursorMax));
        cursorTop = Math.Clamp(cursorTop, 0, Math.Max(0, height - 1));

        int cursorLeft = Math.Clamp(absCursorLeft, 0, Math.Max(0, width - 1));

        // Hide cursor when approval dialog is active
        bool cursorVisible = !showDialog;

        return new LumenFrame(lines, new CursorPosition(cursorLeft, cursorTop, cursorVisible), width, height);
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

    private List<DisplayLine> BuildDialogLines(Claude4Net.Cli.Ui.Approval.ApprovalDialogState dialog, int width, int maxHeight)
    {
        if (maxHeight <= 0)
        {
            return new List<DisplayLine>();
        }

        if (maxHeight == 1)
        {
            return new List<DisplayLine>
            {
                new DisplayLine(FormatDialogRow("! Approval Required [Narrow]", width), DisplayLineKind.Dialog)
            };
        }

        if (maxHeight == 2)
        {
            return new List<DisplayLine>
            {
                new DisplayLine(FormatDialogBorder("Approval Required [Too Small]", '─', '┌', '┐', width), DisplayLineKind.Dialog),
                new DisplayLine(FormatDialogBorder(null, '─', '└', '┘', width), DisplayLineKind.Dialog)
            };
        }

        var finalLines = new List<DisplayLine>();

        // Margin space for borders
        int contentWidth = Math.Max(10, width - 4);

        // 1. Gather all content rows
        var rows = new List<string>();

        // Add risk level and description
        rows.Add($"Risk Level: {dialog.RiskLevel}");

        if (!string.IsNullOrEmpty(dialog.Description))
        {
            var wrappedDesc = TerminalText.WrapByDisplayWidth(dialog.Description, contentWidth);
            rows.AddRange(wrappedDesc);
        }

        // Add key hints
        rows.Add("[Y/Enter] Approve  [N] Deny  [D] Toggle Details  [Esc] Cancel");

        // Gather details rows if in detail mode
        var detailRows = new List<string>();
        if (dialog.IsDetailMode && !string.IsNullOrEmpty(dialog.PreviewSummary))
        {
            var rawDetails = dialog.PreviewSummary.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var detail in rawDetails)
            {
                var wrapped = TerminalText.WrapByDisplayWidth(detail, contentWidth);
                detailRows.AddRange(wrapped);
            }
        }

        // Determine layout constraints
        // Detail mode is only used if there is enough height to fit at least 1 detail line (requires at least 5 lines)
        bool useDetailMode = dialog.IsDetailMode && detailRows.Count > 0 && maxHeight >= 5;
        int reservedBorders = useDetailMode ? 3 : 2;
        int maxContentLines = maxHeight - reservedBorders;

        // 2. Format borders
        string topBorder = FormatDialogBorder(dialog.Title, '─', '┌', '┐', width);
        string divider = FormatDialogBorder("Details", '─', '├', '┤', width);
        string bottomBorder = FormatDialogBorder(null, '─', '└', '┘', width);

        finalLines.Add(new DisplayLine(topBorder, DisplayLineKind.Dialog));

        int currentContentCount = 0;

        // Render main content
        int mainContentLimit = maxContentLines;
        if (useDetailMode)
        {
            mainContentLimit = Math.Max(1, maxContentLines - 1);
        }

        foreach (var row in rows)
        {
            if (currentContentCount >= mainContentLimit) break;
            finalLines.Add(new DisplayLine(FormatDialogRow(row, width), DisplayLineKind.Dialog));
            currentContentCount++;
        }

        // Render detail content if space permits
        if (useDetailMode && detailRows.Count > 0 && currentContentCount < maxContentLines)
        {
            finalLines.Add(new DisplayLine(divider, DisplayLineKind.Dialog));
            int remainingDetailSpace = maxContentLines - currentContentCount;
            int detailCount = 0;

            foreach (var dRow in detailRows)
            {
                if (detailCount >= remainingDetailSpace) break;
                finalLines.Add(new DisplayLine(FormatDialogRow(dRow, width), DisplayLineKind.Dialog));
                detailCount++;
            }
        }

        finalLines.Add(new DisplayLine(bottomBorder, DisplayLineKind.Dialog));

        // Hard guarantee: never exceed maxHeight
        while (finalLines.Count > maxHeight)
        {
            finalLines.RemoveAt(finalLines.Count - 1);
        }

        return finalLines;
    }

    private string FormatDialogRow(string content, int width)
    {
        int targetWidth = Math.Max(0, width - 4);
        int currentWidth = TerminalText.DisplayWidth(content);

        if (currentWidth < targetWidth)
        {
            content = content + new string(' ', targetWidth - currentWidth);
        }
        else if (currentWidth > targetWidth)
        {
            content = TerminalText.TruncateByDisplayWidth(content, targetWidth, "");
        }

        return "│ " + content + " │";
    }

    private string FormatDialogBorder(string? label, char borderChar, char leftCorner, char rightCorner, int width)
    {
        int targetBodyWidth = Math.Max(0, width - 2);

        if (string.IsNullOrEmpty(label))
        {
            return leftCorner + new string(borderChar, targetBodyWidth) + rightCorner;
        }

        string paddedLabel = $" {label} ";
        int labelWidth = TerminalText.DisplayWidth(paddedLabel);

        if (labelWidth >= targetBodyWidth)
        {
            string truncated = TerminalText.TruncateByDisplayWidth(paddedLabel, targetBodyWidth, "");
            return leftCorner + truncated + rightCorner;
        }

        int remaining = targetBodyWidth - labelWidth;
        int leftCount = remaining / 2;
        int rightCount = remaining - leftCount;

        string body = new string(borderChar, leftCount) + paddedLabel + new string(borderChar, rightCount);
        return leftCorner + body + rightCorner;
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

    private List<DisplayLine> BuildPaletteLines(LumenState state, int width)
    {
        var filtered = Claude4Net.Commands.CommandRegistry.GetCommands()
            .OrderBy(c => c.Name)
            .ToList();

        if (!string.IsNullOrEmpty(state.PaletteFilterText))
        {
            filtered = filtered
                .Where(c => c.Name.StartsWith(state.PaletteFilterText, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        int N = filtered.Count;
        if (N == 0)
        {
            var noMatchLines = new List<DisplayLine>();
            string top = FormatDialogBorder("Commands", '─', '┌', '┐', width);
            string middle = FormatDialogRow("No matching commands", width);
            string bottom = FormatDialogBorder(null, '─', '└', '┘', width);
            noMatchLines.Add(new DisplayLine(top, DisplayLineKind.Dialog));
            noMatchLines.Add(new DisplayLine(middle, DisplayLineKind.Dialog));
            noMatchLines.Add(new DisplayLine(bottom, DisplayLineKind.Dialog));
            return noMatchLines;
        }

        int K = Math.Min(5, N);

        int startIndex = state.PaletteSelectedIndex - 2;
        if (startIndex < 0) startIndex = 0;
        if (startIndex > N - K) startIndex = N - K;
        if (startIndex < 0) startIndex = 0;

        var paletteLines = new List<DisplayLine>();
        string topBorder = FormatDialogBorder("Commands", '─', '┌', '┐', width);
        paletteLines.Add(new DisplayLine(topBorder, DisplayLineKind.Dialog));

        for (int i = 0; i < K; i++)
        {
            int itemIndex = startIndex + i;
            var cmd = filtered[itemIndex];
            bool isSelected = itemIndex == state.PaletteSelectedIndex;

            string prefix = isSelected ? "> " : "  ";
            string cmdString = $"{prefix}/{cmd.Name}";
            if (!string.IsNullOrEmpty(cmd.Description))
            {
                cmdString += $" - {cmd.Description}";
            }

            string middle = FormatDialogRow(cmdString, width);
            paletteLines.Add(new DisplayLine(middle, DisplayLineKind.Dialog));
        }

        string bottomBorder = FormatDialogBorder(null, '─', '└', '┘', width);
        paletteLines.Add(new DisplayLine(bottomBorder, DisplayLineKind.Dialog));

        return paletteLines;
    }
}
