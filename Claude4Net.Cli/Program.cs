using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Spectre.Console;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Api;
using Claude4Net.Tools;
using Claude4Net.Commands;
using Claude4Net.Discord;
using System.IO;

// --- 1. DI Setup ---
var services = new ServiceCollection();

// HTTP Client Factory
services.AddHttpClient();

// Messaging
services.AddSingleton<IInputBroker, ChannelBroker>();

// Discord
services.AddSingleton<DiscordListenerService>();

// Tools
services.AddSingleton<LspClient>();
services.AddSingleton<ITool, LspTool>();
services.AddSingleton<ITool, BashTool>();
services.AddSingleton<ITool, FileReadTool>();
services.AddSingleton<ITool, FileWriteTool>();
services.AddSingleton<ITool, FileEditTool>();
services.AddSingleton<ITool, LsTool>();

// --- Dynamic Plugin Loader (Now handled inside ToolOrchestrator) ---
string pluginsPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "plugins");

// Runtime
services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>();
services.AddSingleton<ToolOrchestrator>(sp => new ToolOrchestrator(sp.GetServices<ITool>(), sp.GetService<IUserApprovalHandler>(), sp));
services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolOrchestrator>());

// Api
services.AddSingleton<AnthropicClient>(sp => 
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = clientFactory.CreateClient("Anthropic");
    return new AnthropicClient(httpClient);
});
services.AddSingleton<ClaudeService>();
services.AddSingleton<GeminiProvider>(sp => 
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = clientFactory.CreateClient("Gemini");
    httpClient.Timeout = TimeSpan.FromSeconds(180);
    return new GeminiProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
});
services.AddSingleton<GeminiCliProvider>();
services.AddSingleton<OllamaProvider>(sp => 
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = clientFactory.CreateClient("Ollama");
    httpClient.Timeout = TimeSpan.FromSeconds(300);
    return new OllamaProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
});

var serviceProvider = services.BuildServiceProvider();

// Load initial dynamic plugins using RAM-bound Byte Array Loader
var orchestrator = serviceProvider.GetRequiredService<ToolOrchestrator>();
orchestrator.ReloadDynamicPlugins(pluginsPath);

// --- 2. Initialize and Start ---
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");
AnsiConsole.MarkupLine("[grey]Tip: Press [bold white]ESC[/] during execution to cancel current task.[/]\n");

var broker = serviceProvider.GetRequiredService<IInputBroker>();
var mainCts = new CancellationTokenSource();

// ESC Key Monitor Task
_ = Task.Run(() =>
{
    while (true)
    {
        if (Console.KeyAvailable)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape)
            {
                mainCts.Cancel();
                AnsiConsole.MarkupLine("\n[bold red]✖ Cancellation requested via ESC.[/]");
            }
        }
        Thread.Sleep(100);
    }
});

// Start Discord Listener
var discordService = serviceProvider.GetRequiredService<DiscordListenerService>();
_ = discordService.StartAsync();

// CLI Producer Task
_ = Task.Run(async () =>
{
    var cliOutput = new CliOutputHandler();
    while (true)
    {
        try
        {
            Console.Write("> ");
            string? input = Console.ReadLine();
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

            // Push to broker with CLI context
            broker.TryWrite(new InputContext(input, cliOutput));
        }
        catch (Exception ex)
        {
            AnsiConsole.Console.Write(new Markup($"[bold red][[CLI]] Error:[/] {Markup.Escape(ex.Message)}\n"));
        }
    }
});

// Agent Consumer Loop
while (true)
{
    // Reset CTS for each new agent loop cycle if needed, 
    // or manage it per-request within AgentLoop.
    if (mainCts.IsCancellationRequested) 
    {
        mainCts = new CancellationTokenSource();
    }

    var agent = new AgentLoop(
        serviceProvider.GetRequiredService<ToolOrchestrator>(), 
        serviceProvider, 
        broker);
    
    try 
    {
        await agent.ListenAsync(mainCts.Token);
    }
    catch (OperationCanceledException)
    {
        // Handled within loop or reset for next
    }
}

// --- 3. Helper Classes ---
public class CliOutputHandler : IOutputHandler
{
    public Task WriteAsync(string text) => Task.CompletedTask;

    public Task SendFileAsync(string filePath, string? text = null)
    {
        if (!string.IsNullOrEmpty(text)) 
            AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] {Markup.Escape(text)}\n"));
        
        AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] File available at: [underlined]{Markup.Escape(filePath)}[/]\n"));
        return Task.CompletedTask;
    }
}

public class CliUserApprovalHandler : IUserApprovalHandler
{
    public async Task<bool> RequestApprovalAsync(string tool, string args)
    {
        AnsiConsole.MarkupLine($"[yellow]Request:[/] [bold]{Markup.Escape(tool)}[/] {Markup.Escape(args)}");
        
        while (true)
        {
            AnsiConsole.Markup("[bold white]Allow execution? (y/n): [/]");
            // Console.ReadLine() 사용 시 배경 Task와 경쟁할 수 있으므로, 
            // 직접 Console.In.ReadLine() 등을 고려하거나 Spectre Console의 Prompt 사용
            // 하지만 오타 대응을 위해 직접 한 줄 읽고 분석
            string? input = Console.ReadLine()?.Trim().ToLower();
            
            if (string.IsNullOrEmpty(input)) continue;

            if (input.StartsWith("y")) return true;
            if (input.StartsWith("n")) return false;

            AnsiConsole.MarkupLine("[red]Invalid input. Please type 'y' for yes or 'n' for no.[/]");
        }
    }
}
