using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering.HistoryCells;

public class ApprovalCell(string requestId, string title, string description) : HistoryCell
{
    public string RequestId { get; } = requestId;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public bool? IsApproved { get; private set; }

    public void Resolve(bool approved)
    {
        IsApproved = approved;
    }

    public override string ToPlainText()
    {
        var status = IsApproved switch
        {
            true => "APPROVED",
            false => "DENIED",
            _ => "PENDING"
        };
        return $"Approval Required [{status}]: {Title} - {Description}";
    }

    public override IRenderable GetRenderable()
    {
        var status = IsApproved switch
        {
            true => $"[{LumenTheme.SuccessColor}]APPROVED[/]",
            false => $"[{LumenTheme.ErrorColor}]DENIED[/]",
            _ => "[yellow]PENDING[/]"
        };

        return new Panel(
            new Markup($"{Markup.Escape(Description)}\n\nStatus: {status}")
        )
        {
            Header = new PanelHeader($"Approval Required: {Markup.Escape(Title)}"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
    }
}
