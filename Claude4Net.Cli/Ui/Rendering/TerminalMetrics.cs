using System;

namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// Capabilities and dimensions of the current terminal.
/// </summary>
public sealed record TerminalMetrics(
    int Width,
    int Height,
    bool SupportsAnsi,
    bool IsOutputRedirected);
