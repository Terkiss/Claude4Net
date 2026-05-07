using System;
using System.Text;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// CLI 환경에서 사용자 승인을 처리하는 핸들러입니다.
    /// Program.cs의 비동기 입력 루프와 연동하여 동작합니다.
    /// </summary>
    public class CliUserApprovalHandler : IUserApprovalHandler, IRichApprovalHandler
    {
        public static TaskCompletionSource<string>? PendingApproval;

        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]??  Approval Required:[/] Tool [cyan]{Markup.Escape(tool)}[/] requests execution.");
            AnsiConsole.MarkupLine($"[grey]Arguments: {Markup.Escape(args)}[/]");
            
            return await WaitForApprovalAsync();
        }

        public async Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff)
        {
            AnsiConsole.MarkupLine($"\n[bold yellow]??  Approval Required:[/] Tool [cyan]{Markup.Escape(tool)}[/] requests file modification.");

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

            return await WaitForApprovalAsync();
        }

        private async Task<bool> WaitForApprovalAsync()
        {
            while (true)
            {
                AnsiConsole.Markup("[bold white]Allow action? (y/n): [/]");
                
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
