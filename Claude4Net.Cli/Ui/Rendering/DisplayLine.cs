namespace Claude4Net.Cli.Ui.Rendering;

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
