using Spectre.Console;
using Spectre.Console.Rendering;
using Claude4Net.Cli.Ui.Input;

namespace Claude4Net.Cli.Ui.Rendering;

public class BottomPane
{
    public IRenderable Render(LumenState state, PromptComposerState composerState)
    {
        if (state.ApprovalDialog.IsVisible)
        {
            // If dialog is visible, we don't show the prompt composer
            return new Text(string.Empty);
        }

        var prefix = new Markup($"[{LumenTheme.UserColor}]{LumenTheme.PromptSymbol}[/]");

        if (string.IsNullOrEmpty(composerState.Text))
        {
            return new Rows(prefix, new Markup("[grey]Type your message here...[/]"));
        }

        // Simple rendering of current buffer
        return new Rows(prefix, new Text(composerState.Text));
    }
}
