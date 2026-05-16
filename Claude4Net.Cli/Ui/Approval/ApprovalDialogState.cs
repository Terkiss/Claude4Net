using System;

namespace Claude4Net.Cli.Ui.Approval
{
    public enum ApprovalDialogAction
    {
        None,
        Approve,
        Deny,
        ToggleDetails,
        Cancel
    }

    public sealed record ApprovalDialogState
    {
        public string RequestId { get; init; } = "";
        public string Title { get; init; } = "Approval Required";
        public string Description { get; init; } = "";
        public string RiskLevel { get; init; } = "Normal";
        public string PreviewSummary { get; init; } = "";
        public bool IsVisible { get; init; }
        public bool IsDetailMode { get; init; }
        public ApprovalDialogAction LastAction { get; init; } = ApprovalDialogAction.None;

        public static readonly ApprovalDialogState Hidden = new();
    }
}
