using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Api;
using Claude4Net.Tools;
using Claude4Net.Commands;

// --- 1. DI Setup ---
var services = new ServiceCollection();

// Tools
services.AddSingleton<ITool, BashTool>();
services.AddSingleton<ITool, FileReadTool>();
services.AddSingleton<ITool, FileWriteTool>();
services.AddSingleton<ITool, FileEditTool>();
services.AddSingleton<ITool, LsTool>();

// Runtime
services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>();
services.AddSingleton<ToolOrchestrator>(sp => new ToolOrchestrator(sp.GetServices<ITool>(), sp.GetService<IUserApprovalHandler>()));
services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolOrchestrator>());

// Api
services.AddSingleton<AnthropicClient>(sp => new AnthropicClient());
services.AddSingleton<ClaudeService>();
services.AddSingleton<GeminiProvider>(sp => new GeminiProvider(sp.GetRequiredService<IToolRegistry>()));
services.AddSingleton<OllamaProvider>(sp => new OllamaProvider(sp.GetRequiredService<IToolRegistry>()));

var serviceProvider = services.BuildServiceProvider();

// --- 2. Main Loop ---
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");

while (true)
{
    string input = AnsiConsole.Ask<string>("[bold green]>[/]");
    if (string.IsNullOrWhiteSpace(input)) continue;

    if (input.StartsWith("!") || input.StartsWith("/"))
    {
        string cmdName = input.Split(' ')[0];
        string cmdArgs = input.Contains(' ') ? input.Substring(input.IndexOf(' ') + 1) : "";
        
        var cmd = CommandRegistry.FindCommand(cmdName);
        if (cmd != null && cmd.Handler != null)
        {
            var res = await cmd.Handler(cmdArgs, serviceProvider);
            AnsiConsole.MarkupLine(res);
            continue;
        }
    }

    // Default Agent Interaction
    ILLMProvider provider;
    if (AppState.ActiveProvider == "gemini") provider = serviceProvider.GetRequiredService<GeminiProvider>();
    else if (AppState.ActiveProvider == "ollama") provider = serviceProvider.GetRequiredService<OllamaProvider>();
    else provider = serviceProvider.GetRequiredService<ClaudeService>();

    var agent = new AgentLoop(provider, serviceProvider.GetRequiredService<ToolOrchestrator>(), serviceProvider);
    await agent.RunAsync(input);
}

// --- 3. Helper Classes ---
public class CliUserApprovalHandler : IUserApprovalHandler
{
    public Task<bool> RequestApprovalAsync(string tool, string args)
    {
        AnsiConsole.MarkupLine($"[yellow]Request:[/] [bold]{tool}[/] {Markup.Escape(args)}");
        return Task.FromResult(AnsiConsole.Confirm("Allow execution?"));
    }
}
