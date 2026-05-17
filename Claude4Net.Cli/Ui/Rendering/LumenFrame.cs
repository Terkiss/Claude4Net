using System;
using System.Collections.Generic;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Represents a pure, immutable frame of the Lumen UI.
/// </summary>
public sealed record LumenFrame(
    IReadOnlyList<DisplayLine> Lines,
    CursorPosition Cursor,
    int Width,
    int Height);

/// <summary>
/// A single line of text in a frame.
/// </summary>
public sealed record DisplayLine(
    string Text,
    DisplayLineKind Kind);

/// <summary>
/// Categorizes lines for rendering and scroll logic.
/// </summary>
public enum DisplayLineKind
{
    Transcript,
    Separator,
    Input,
    Footer,
    Dialog
}

/// <summary>
/// Cursor state in a frame.
/// </summary>
public sealed record CursorPosition(
    int Left,
    int Top,
    bool Visible);
