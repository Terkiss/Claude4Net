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
}
