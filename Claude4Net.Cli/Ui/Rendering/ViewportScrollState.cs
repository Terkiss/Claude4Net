namespace Claude4Net.Cli.Ui.Rendering;

/// <summary>
/// State for managing viewport scrolling.
/// </summary>
public sealed record ViewportScrollState(
    int ScrollOffset = 0,
    bool AutoScroll = true);
