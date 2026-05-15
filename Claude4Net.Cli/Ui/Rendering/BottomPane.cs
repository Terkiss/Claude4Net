using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class BottomPane
{
    public IRenderable Render(LumenState state)
    {
        // Placeholder for prompt composer
        return new Markup($"[{LumenTheme.UserColor}]{LumenTheme.PromptSymbol}[/][grey]Type your message here...[/]");
    }
}
