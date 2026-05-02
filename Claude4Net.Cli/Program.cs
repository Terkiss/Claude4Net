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
services.AddSingleton<ISmartRouter, SmartRouter>();
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
services.AddSingleton<IEmbeddingProvider, GeminiEmbeddingProvider>(sp => 
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = clientFactory.CreateClient("Gemini");
    return new GeminiEmbeddingProvider(httpClient);
});
services.AddSingleton<OllamaProvider>(sp => 
{
    var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = clientFactory.CreateClient("Ollama");
    httpClient.Timeout = TimeSpan.FromSeconds(300);
    return new OllamaProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
});

var serviceProvider = services.BuildServiceProvider();

// --- Non-interactive Smoke Test Path ---
if (args.Contains("--smoke-exit"))
{
    var cmd = CommandRegistry.FindCommand("/exit");
    if (cmd != null && cmd.Handler != null)
    {
        var res = await cmd.Handler("", serviceProvider);
        Console.WriteLine(res);
        Console.Out.Flush();
        return 0;
    }
    else
    {
        Console.Error.WriteLine("Error: /exit command not found in Registry.");
        return 1;
    }
}

// Load initial dynamic plugins using RAM-bound Byte Array Loader
var orchestrator = serviceProvider.GetRequiredService<ToolOrchestrator>();
orchestrator.ReloadDynamicPlugins(pluginsPath);

// --- 2. Initialize and Start ---
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");
AnsiConsole.MarkupLine("[grey]Tip: Press [bold white]ESC[/] during execution to cancel current task.[/]\n");

var broker = serviceProvider.GetRequiredService<IInputBroker>();
var mainCts = new CancellationTokenSource();

if (Console.IsInputRedirected)
{
    // --- Redirected Input Path (Piped) ---
    var cliOutput = new CliOutputHandler();
    string? rawLine;
    while (!mainCts.Token.IsCancellationRequested && (rawLine = Console.ReadLine()) != null)
    {
        string input = rawLine.Trim();
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
                Console.Out.Flush();
                
                if (cmd.Name == "exit")
                {
                    mainCts.Cancel();
                    break;
                }
                continue;
            }
        }

        // Process as normal prompt via AgentLoop
        var router = serviceProvider.GetRequiredService<ISmartRouter>();
        var decision = router.Route(input);
        ILLMProvider provider = decision.SelectedProvider switch
        {
            "gemini" => serviceProvider.GetRequiredService<GeminiProvider>(),
            "gemini-cli" => serviceProvider.GetRequiredService<GeminiCliProvider>(),
            "ollama" => serviceProvider.GetRequiredService<OllamaProvider>(),
            _ => serviceProvider.GetRequiredService<ClaudeService>()
        };

        var agent = new AgentLoop(
            serviceProvider.GetRequiredService<ToolOrchestrator>(), 
            serviceProvider, 
            broker,
            router,
            serviceProvider.GetRequiredService<IEmbeddingProvider>());
            
        try
        {
            await agent.RunAsync(input, cliOutput, provider, decision.SelectedModel, mainCts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
        }
    }
}
else
{
    // --- Interactive Terminal Path (Producer-Consumer) ---
    
    // ESC Key Monitor Task
    _ = Task.Run(() =>
    {
        while (!mainCts.Token.IsCancellationRequested)
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
    var producerTask = Task.Run(async () =>
    {
        var cliOutput = new CliOutputHandler();
        while (!mainCts.Token.IsCancellationRequested)
        {
            try
            {
                if (CliUserApprovalHandler.PendingApproval == null)
                    Console.Write("> ");
                    
                string? rawInput = Console.ReadLine();
                if (rawInput == null) 
                {
                    // EOF reached (e.g. piped input ended)
                    mainCts.Cancel();
                    break;
                }

                string input = rawInput.Trim();

                if (CliUserApprovalHandler.PendingApproval != null)
                {
                    var tcs = CliUserApprovalHandler.PendingApproval;
                    CliUserApprovalHandler.PendingApproval = null;
                    tcs.TrySetResult(input);
                    continue;
                }

                // 붙여넣기(Paste)로 인한 멀티라인(개행) 폭탄 방어 로직
                var sb = new System.Text.StringBuilder(input);
                System.Threading.Thread.Sleep(15); 
                while (Console.KeyAvailable)
                {
                    string? nextLine = Console.ReadLine();
                    if (nextLine != null)
                    {
                        sb.AppendLine();
                        sb.Append(nextLine);
                    }
                    System.Threading.Thread.Sleep(15);
                }
                input = sb.ToString();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.StartsWith("!") || input.StartsWith("/"))
                {
                    string[] parts = input.Split(' ', 2);
                    string cmdName = parts[0];
                    string cmdArgs = parts.Length > 1 ? parts[1] : "";

                    var cmd = CommandRegistry.FindCommand(cmdName);
                    if (cmd != null && cmd.Handler != null)
                    {
                        var res = await cmd.Handler(cmdArgs, serviceProvider);
                        AnsiConsole.MarkupLine(res);
                        Console.Out.Flush();
                        
                        if (cmd.Name == "exit")
                        {
                            mainCts.Cancel();
                            break;
                        }
                        continue;
                    }
                }

                broker.TryWrite(new InputContext(input, cliOutput));
            }
            catch (Exception ex)
            {
                AnsiConsole.Console.Write(new Markup($"[bold red][[CLI]] Error:[/] {Markup.Escape(ex.Message)}\n"));
            }
        }
    });

    // Agent Consumer Loop
    while (!mainCts.Token.IsCancellationRequested)
    {
        var agent = new AgentLoop(
            serviceProvider.GetRequiredService<ToolOrchestrator>(), 
            serviceProvider, 
            broker,
            serviceProvider.GetRequiredService<ISmartRouter>(),
            serviceProvider.GetRequiredService<IEmbeddingProvider>());
        
        try 
        {
            await agent.ListenAsync(mainCts.Token);
        }
        catch (OperationCanceledException) { }
    }

    await producerTask;
}

AnsiConsole.MarkupLine("[grey]Exiting main loop...[/]");
Console.Out.Flush();
System.Threading.Thread.Sleep(200);
return 0;



// --- 3. Helper Classes ---
public class CliOutputHandler : IOutputHandler
{
    public Task WriteAsync(string text) => Task.CompletedTask;

    public Task CompleteAsync(string finalMessage) => Task.CompletedTask;

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
    public static System.Threading.Tasks.TaskCompletionSource<string>? PendingApproval;

    public async Task<bool> RequestApprovalAsync(string tool, string args)
    {
        AnsiConsole.MarkupLine($"[yellow]Request:[/] [bold]{Markup.Escape(tool)}[/] {Markup.Escape(args)}");
        
        while (true)
        {
            AnsiConsole.Markup("[bold white]Allow execution? (y/n): [/]");
            
            var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
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
