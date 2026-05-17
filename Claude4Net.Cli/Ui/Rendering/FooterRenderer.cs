using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class FooterRenderer
{
    public IRenderable Render(LumenState state)
    {
        var width = AnsiConsole.Console.Profile.Width;
        var provider = state.Provider ?? "None";
        var model = state.Model ?? "None";
        var sessionId = state.SessionId ?? "None";
        var status = state.IsRunning ? "[green]RUNNING[/]" : "[grey]IDLE[/]";

        string footerText;
        if (width < 60)
        {
            footerText = $" {status} ";
        }
        else if (width < 90)
        {
            footerText = $" {status} | P: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | M: [{LumenTheme.MetadataColor}]{Markup.Escape(model)}[/] ";
        }
        else
        {
            footerText = $" {status} | Provider: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | Model: [{LumenTheme.MetadataColor}]{Markup.Escape(model)}[/] | Session: [{LumenTheme.MetadataColor}]{Markup.Escape(sessionId)}[/] ";
        }

        return new Rule(footerText)
        {
            Justification = Justify.Left,
            Style = new Style(Color.Grey35)
        };
    }
}
