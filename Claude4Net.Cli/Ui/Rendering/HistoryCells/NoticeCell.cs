using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class NoticeCell(string message, string level = "Info") : HistoryCell
{
    public string Message { get; } = message;
    public string Level { get; } = level;

    public override string ToPlainText() => $"[{Level.ToUpper()}]: {Message}";

    public override IRenderable GetRenderable()
    {
        var color = Level.ToLower() switch
        {
            "error" => LumenTheme.ErrorColor,
            "warning" => LumenTheme.WarningColor,
            _ => LumenTheme.MetadataColor
        };
        return new Markup($"[{color}][[{Markup.Escape(Level.ToUpper())}]][/] {Markup.Escape(Message)}");
    }
}
