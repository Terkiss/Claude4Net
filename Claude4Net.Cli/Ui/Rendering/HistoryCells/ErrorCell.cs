using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ErrorCell(string message, string? details = null) : HistoryCell
{
    public string Message { get; } = message;
    public string? Details { get; } = details;

    public override string ToPlainText() => $"ERROR: {Message}{(Details != null ? $" - {Details}" : "")}";

    public override IRenderable GetRenderable()
    {
        var text = $"[{LumenTheme.ErrorColor}]ERROR:[/] {Markup.Escape(Message)}";
        if (Details != null)
        {
            text += $"\n[grey]{Markup.Escape(Details)}[/]";
        }
        return new Markup(text);
    }
}
