using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Cli.Ui.Rendering;

public class DialogLayer
{
    public IRenderable? Render(LumenState state)
    {
        if (!state.ApprovalDialog.IsVisible)
            return null;

        var d = state.ApprovalDialog;

        // Color based on risk level
        string titleColor = d.RiskLevel.ToLower() switch
        {
            "high" or "critical" => "red",
            "medium" or "warning" => "yellow",
            _ => "blue"
        };

        var elements = new List<IRenderable>();
        elements.Add(new Markup($"[bold {titleColor}]{Markup.Escape(d.Title)}[/]"));
        elements.Add(new Text(d.Description));
        elements.Add(new Text(""));

        if (d.IsDetailMode && !string.IsNullOrWhiteSpace(d.PreviewSummary))
        {
            var panel = new Panel(new Text(d.PreviewSummary))
            {
                Header = new PanelHeader("Details/Diff"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey)
            };
            elements.Add(panel);
            elements.Add(new Text(""));
        }

        elements.Add(new Markup("[grey]Keys: [/][white][[Y]][/] Approve [grey]|[/] [white][[N]][/] Deny [grey]|[/] [white][[D]][/] Toggle Details [grey]|[/] [white][[Esc]][/] Cancel"));

        return new Panel(new Rows(elements))
        {
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Blue),
            Padding = new Padding(1, 1, 1, 1)
        };
    }
}
