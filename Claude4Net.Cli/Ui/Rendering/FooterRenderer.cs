using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class FooterRenderer
{
    public IRenderable Render(LumenState state)
    {
        var provider = state.Provider ?? "None";
        var model = state.Model ?? "None";
        var sessionId = state.SessionId ?? "None";
        var status = state.IsRunning ? "[green]RUNNING[/]" : "[grey]IDLE[/]";

        return new Rule($" {status} | Provider: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | Model: [{LumenTheme.MetadataColor}]{Markup.Escape(model)}[/] | Session: [{LumenTheme.MetadataColor}]{Markup.Escape(sessionId)}[/] ")
        {
            Justification = Justify.Left,
            Style = new Style(Color.Grey35)
        };
    }
}
