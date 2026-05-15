using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class DialogLayer
{
    public IRenderable? Render(LumenState state)
    {
        // Currently no dialog state in LumenState.
        // Returning null as a placeholder.
        return null;
    }
}
