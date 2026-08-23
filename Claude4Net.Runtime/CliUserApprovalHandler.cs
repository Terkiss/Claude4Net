using System;
using System.Text;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Telemetry;
using Claude4Net.Runtime.Telemetry;
using Spectre.Console;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// CLI 환경 및 웹 대시보드 양방향 연동 사용자 승인 핸들러입니다.
    /// Program.cs 및 대시보드 ControlPlaneHub 결재 큐와 실시간 연동됩니다.
    /// </summary>
    public class CliUserApprovalHandler : IUserApprovalHandler, IRichApprovalHandler
    {
        public static TaskCompletionSource<string>? PendingApproval;
        public static string? CurrentTaskId;

        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            string taskId = $"TASK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
            CurrentTaskId = taskId;

            TeruTeruPandasTelemetryEngine.Shared.QueueApprovalRequest(new MasterApprovalItemDto
            {
                TaskId = taskId,
                Title = $"Tool Execution: {tool}",
                Description = $"Tool '{tool}' requested execution with arguments: {args}",
                RequestedBy = "Agent Loop / ToolOrchestrator",
                RiskLevel = "Tier 3 - Dangerous Command",
                TargetEnvironment = "Local Environment",
                RequestedAt = DateTime.UtcNow,
                DiffSummary = $"Tool: {tool}\nArgs: {args}",
                Status = "Pending"
            });

            AnsiConsole.MarkupLine($"\n[bold yellow]⚠️  Approval Required [[{taskId}]]:[/] Tool [cyan]{Markup.Escape(tool)}[/] requests execution.");
            AnsiConsole.MarkupLine($"[grey]Arguments: {Markup.Escape(args)}[/]");
            AnsiConsole.MarkupLine("[dim]You can approve via terminal (y/n) or directly in the Web Dashboard at http://localhost:5000/approvals[/]");
            
            return await WaitForApprovalAsync();
        }

        public async Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff)
        {
            string taskId = $"TASK-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..4]}";
            CurrentTaskId = taskId;

            TeruTeruPandasTelemetryEngine.Shared.QueueApprovalRequest(new MasterApprovalItemDto
            {
                TaskId = taskId,
                Title = $"File Modification: {diff.FilePath}",
                Description = $"Tool '{tool}' requested {diff.ChangeType} on file: {diff.FilePath}",
                RequestedBy = "AGY Worker",
                RiskLevel = "Tier 3 - File Mutation",
                TargetEnvironment = "Local Workspace",
                RequestedAt = DateTime.UtcNow,
                DiffSummary = diff.DiffContent,
                Status = "Pending"
            });

            AnsiConsole.MarkupLine($"\n[bold yellow]⚠️  Approval Required [[{taskId}]]:[/] Tool [cyan]{Markup.Escape(tool)}[/] requests file modification.");

            var grid = new Grid();
            grid.AddColumn();
            grid.AddRow($"[bold cyan]Path:[/] {Markup.Escape(diff.FilePath)}");
            grid.AddRow($"[bold cyan]Type:[/] {diff.ChangeType}");

            var diffBox = new StringBuilder();
            var lines = diff.DiffContent.Split('\n');
            foreach (var line in lines)
            {
                if (line.StartsWith("+")) diffBox.AppendLine($"[green]{Markup.Escape(line)}[/]");
                else if (line.StartsWith("-")) diffBox.AppendLine($"[red]{Markup.Escape(line)}[/]");
                else diffBox.AppendLine(Markup.Escape(line));
            }

            AnsiConsole.Write(new Panel(grid) { Header = new PanelHeader("File Change Summary"), Border = BoxBorder.Rounded });
            AnsiConsole.Write(new Panel(diffBox.ToString().TrimEnd()) { Header = new PanelHeader("Proposed Diff"), Border = BoxBorder.Rounded });
            AnsiConsole.MarkupLine("[dim]You can approve via terminal (y/n) or directly in the Web Dashboard at http://localhost:5000/approvals[/]");

            return await WaitForApprovalAsync();
        }

        private async Task<bool> WaitForApprovalAsync()
        {
            while (true)
            {
                AnsiConsole.Markup("[bold white]Allow action? (y/n) or approve via Web Dashboard: [/]");
                
                var tcs = new TaskCompletionSource<string>();
                PendingApproval = tcs;

                string input = await tcs.Task;
                input = input.Trim().ToLower();
                
                if (string.IsNullOrEmpty(input)) continue;

                if (input.StartsWith("y")) return true;
                if (input.StartsWith("n")) return false;

                AnsiConsole.MarkupLine("[red]Invalid input. Please type 'y' for yes or 'n' for no.[/]");
            }
        }
    }
}
