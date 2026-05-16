namespace Claude4Net.Cli.Ui.Input
{
    /// <summary>
    /// Represents the current snapshot of the prompt composer state.
    /// </summary>
    public sealed record PromptComposerState(string Text, int CursorPosition, string? Suggestion);
}
