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
/// Cursor state in a frame.
/// </summary>
public sealed record CursorPosition(
    int Left,
    int Top,
    bool Visible);
