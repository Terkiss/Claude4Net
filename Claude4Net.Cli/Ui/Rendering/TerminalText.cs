using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Utilities for display-width-aware text handling in terminals.
/// </summary>
public static partial class TerminalText
{
    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]")]
    private static partial Regex AnsiRegex();

    /// <summary>
    /// Removes ANSI escape sequences from the text.
    /// </summary>
    public static string StripAnsi(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return AnsiRegex().Replace(text, "");
    }

    /// <summary>
    /// Calculates the total display width of the given text, ignoring ANSI escape sequences.
    /// Hangul and CJK characters count as 2, ASCII as 1, and combining marks as 0.
    /// </summary>
    public static int DisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int totalWidth = 0;
        bool inAnsi = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (ProcessAnsiState(ref inAnsi, rune)) continue;
            totalWidth += GetRuneWidth(rune);
        }

        return totalWidth;
    }

    /// <summary>
    /// Wraps text into multiple lines based on display width, preserving ANSI sequences.
    /// </summary>
    public static IReadOnlyList<string> WrapByDisplayWidth(string? text, int width)
    {
        if (width <= 0) return Array.Empty<string>();
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        var lines = new List<string>();
        var currentLine = new StringBuilder();
        int currentLineWidth = 0;

        bool inAnsi = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (ProcessAnsiState(ref inAnsi, rune))
            {
                currentLine.Append(rune.ToString());
                continue;
            }

            int runeWidth = GetRuneWidth(rune);

            if (rune.Value == '\n')
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLineWidth = 0;
                continue;
            }

            if (currentLine.Length > 0 && currentLineWidth + runeWidth > width)
            {
                lines.Add(currentLine.ToString());
                currentLine.Clear();
                currentLineWidth = 0;
            }

            currentLine.Append(rune.ToString());
            currentLineWidth += runeWidth;
        }

        if (currentLine.Length > 0)
        {
            lines.Add(currentLine.ToString());
        }

        return lines;
    }

    /// <summary>
    /// Truncates text to fit within a specified display width, appending a suffix if needed.
    /// </summary>
    public static string TruncateByDisplayWidth(string? text, int width, string suffix = "...")
    {
        if (width <= 0) return string.Empty;
        if (string.IsNullOrEmpty(text)) return string.Empty;

        int textWidth = DisplayWidth(text);
        if (textWidth <= width) return text;

        int suffixWidth = DisplayWidth(suffix);
        int availableWidth = width - suffixWidth;

        if (availableWidth <= 0)
        {
            return TruncateSimple(suffix, width);
        }

        var result = new StringBuilder();
        int currentWidth = 0;
        bool inAnsi = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (ProcessAnsiState(ref inAnsi, rune))
            {
                result.Append(rune.ToString());
                continue;
            }

            int runeWidth = GetRuneWidth(rune);
            if (currentWidth + runeWidth > availableWidth) break;

            result.Append(rune.ToString());
            currentWidth += runeWidth;
        }

        result.Append(suffix);
        return result.ToString();
    }

    private static string TruncateSimple(string text, int width)
    {
        var result = new StringBuilder();
        int currentWidth = 0;
        bool inAnsi = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (ProcessAnsiState(ref inAnsi, rune))
            {
                result.Append(rune.ToString());
                continue;
            }

            int runeWidth = GetRuneWidth(rune);
            if (currentWidth + runeWidth > width) break;
            result.Append(rune.ToString());
            currentWidth += runeWidth;
        }
        return result.ToString();
    }

    private static bool ProcessAnsiState(ref bool inAnsi, Rune rune)
    {
        if (rune.Value == 0x1B) // ESC
        {
            inAnsi = true;
            return true;
        }

        if (inAnsi)
        {
            // ANSI CSI sequences start with '[' (0x5B) and end with a character in 0x40-0x7E.
            // We should not end the ANSI sequence on the '[' itself.
            if (rune.Value != '[' && rune.Value >= 0x40 && rune.Value <= 0x7E)
            {
                inAnsi = false;
            }
            return true;
        }

        return false;
    }

    private static int GetRuneWidth(Rune rune)
    {
        if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark) return 0;
        if (rune.Value < 0x20) return 0;
        if (rune.Value <= 0x7F) return 1;
        if (IsCjk(rune)) return 2;
        return 1;
    }

    private static bool IsCjk(Rune rune)
    {
        int v = rune.Value;
        if (v >= 0x4E00 && v <= 0x9FFF) return true;
        if (v >= 0x3400 && v <= 0x4DBF) return true;
        if (v >= 0xAC00 && v <= 0xD7AF) return true;
        if (v >= 0x1100 && v <= 0x11FF) return true;
        if (v >= 0x3130 && v <= 0x318F) return true;
        if (v >= 0xFF00 && v <= 0xFFEF) return true;
        if (v >= 0x3000 && v <= 0x303F) return true;
        if (v >= 0x20000 && v <= 0x3FFFF) return true;
        return false;
    }
}
