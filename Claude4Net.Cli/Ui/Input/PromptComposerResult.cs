namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Status of the prompt composition after processing a key.
    /// </summary>
    public enum PromptComposerStatus
    {
        /// <summary> Still editing. </summary>
        Editing,
        /// <summary> Submitted via Enter. </summary>
        Submitted,
        /// <summary> Cancelled via Escape or Ctrl+C. </summary>
        Cancelled,
        /// <summary> Clear screen signal via Ctrl+L. </summary>
        ClearSignal,
        /// <summary> Scroll signal via PageUp/Down etc. </summary>
        Scrolled
    }

    /// <summary>
    /// Result of a single key process in the prompt composer.
    /// </summary>
    public sealed record PromptComposerResult(PromptComposerStatus Status, string? Text, InputAction Action = InputAction.None);
}
