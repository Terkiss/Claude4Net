using System;
using Spectre.Console;
using Spectre.Console.Rendering;
using Claude4Net.SDK;

namespace Claude4Net.Cli.Ui.Rendering;

public class FooterRenderer
{
    public IRenderable Render(LumenState state)
    {
        var width = AnsiConsole.Console.Profile.Width;
        var provider = state.Provider ?? "None";
        var model = state.Model ?? "None";
        var sessionId = state.SessionId ?? "None";

        string spinner = "";
        if (state.IsRunning)
        {
            string[] frames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
            int index = Math.Abs((int)((DateTime.UtcNow.Ticks / 1000000) % frames.Length));
            spinner = frames[index] + " ";
        }
        var status = state.IsRunning ? $"[green]{spinner}RUNNING[/]" : "[grey]IDLE[/]";
        string themeName = LumenTheme.UserColor == "springgreen1" ? "neon" : (LumenTheme.UserColor == "darkgreen" ? "light" : "dark");
        string yolo = AppState.CurrentPermissionMode == PermissionMode.Yolo ? " | [red]YOLO[/]" : "";

        string footerText;
        if (width < 50)
        {
            footerText = $" {status}{yolo} ";
        }
        else if (width < 80)
        {
            footerText = $" {status} | P: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | T: [{LumenTheme.MetadataColor}]{themeName}[/]{yolo} ";
        }
        else if (width < 110)
        {
            footerText = $" {status} | Provider: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | Model: [{LumenTheme.MetadataColor}]{Markup.Escape(model)}[/] | Theme: [{LumenTheme.MetadataColor}]{themeName}[/]{yolo} ";
        }
        else
        {
            footerText = $" {status} | Provider: [{LumenTheme.MetadataColor}]{Markup.Escape(provider)}[/] | Model: [{LumenTheme.MetadataColor}]{Markup.Escape(model)}[/] | Theme: [{LumenTheme.MetadataColor}]{themeName}[/] | Session: [{LumenTheme.MetadataColor}]{Markup.Escape(sessionId)}[/]{yolo} ";
        }

        // Parse style safely
        var borderStyle = new Style(foreground: Color.Grey35);
        try
        {
            borderStyle = Style.Parse(LumenTheme.BorderColor);
        }
        catch { }

        return new Rule(footerText)
        {
            Justification = Justify.Left,
            Style = borderStyle
        };
    }
}
