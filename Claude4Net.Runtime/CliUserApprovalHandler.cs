using System;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Runtime
{
    public class CliUserApprovalHandler : IUserApprovalHandler
    {
        public static TaskCompletionSource<string>? PendingApproval;

        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            AnsiConsole.MarkupLine($"[yellow]Request:[/] [bold]{Markup.Escape(tool)}[/] {Markup.Escape(args)}");
            
            while (true)
            {
                AnsiConsole.Markup("[bold white]Allow execution? (y/n): [/]");
                
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
