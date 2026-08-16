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
using Claude4Net.Dashboard;
using Claude4Net.Cli.Bootstrap;
using System.IO;
using System.Threading;

// ============================================================================
// Claude4Net CLI Entry Point
// ============================================================================

var options = CliOptions.Parse(args);

if (options.WorkspaceDir != null)
{
    if (!Directory.Exists(options.WorkspaceDir))
    {
        Console.Error.WriteLine($"Error: workspace directory '{options.WorkspaceDir}' does not exist.");
        return 1;
    }
    AppState.CurrentCwd = Path.GetFullPath(options.WorkspaceDir);
}

// Load configuration and resolve provider/model settings precedence
var globalConfig = await SettingsManager.GetMergedSettingsAsync();
SettingsManager.ApplyPrecedence(globalConfig, options.Provider, options.Model);

// --- 1. Dependency Injection Configuration ---
var services = new ServiceCollection();
CliServiceRegistration.ConfigureServices(services);

if (options.StartDashboard)
{
    AnsiConsole.MarkupLine("[grey][[INFO]] Web Dashboard starting on http://localhost:5000...[/]");
    try
    {
        await DashboardServer.StartAsync(Array.Empty<string>(), DashboardServer.DefaultPort);
        AnsiConsole.MarkupLine("[bold green][[OK]] Web Dashboard started at http://localhost:5000[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[bold red][[ERROR]] Web Dashboard failed to start:[/] [yellow]{Markup.Escape(ex.Message)}[/]");
        AnsiConsole.MarkupLine("[grey][[INFO]] CLI will continue without Dashboard.[/]");
    }
}

var serviceProvider = services.BuildServiceProvider();

if (options.StartApi)
{
    var apiServer = serviceProvider.GetRequiredService<Claude4Net.Runtime.ApiServer.Claude4NetApiServer>();
    try
    {
        await apiServer.StartAsync(options.ApiPort, options.ApiKey);
        AnsiConsole.MarkupLine($"[bold green][[OK]] In-Process OpenAI API Server started at http://localhost:{options.ApiPort}[/]");
        AnsiConsole.MarkupLine($"[grey]      Bearer Auth Key:[/] [cyan]{Markup.Escape(apiServer.ApiKey)}[/]");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[bold red][[ERROR]] API Server failed to start:[/] [yellow]{Markup.Escape(ex.Message)}[/]");
    }
}

// Handle Permission Mode
if (options.PermissionModeArg != null)
{
    if (CliOptions.TryParsePermissionMode(options.PermissionModeArg, out var parsedMode))
    {
        AppState.CurrentPermissionMode = parsedMode;
    }
    else
    {
        Console.Error.WriteLine($"Error: invalid permission mode '{options.PermissionModeArg}'.");
        return 1;
    }
}

// Doctor Command Path
if (options.IsDoctor)
{
    var cmd = CommandRegistry.FindCommand("/doctor");
    if (cmd?.Handler == null)
    {
        Console.Error.WriteLine("Error: /doctor command not found in Registry.");
        return 1;
    }

    var res = await cmd.Handler(options.DoctorArgs ?? "", serviceProvider);
    Console.WriteLine(res);
    Console.Out.Flush();
    return 0;
}

// Smoke Test Path
if (options.SmokeExit)
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

// Dynamic Plugin Loading
string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
var orchestrator = serviceProvider.GetRequiredService<ToolOrchestrator>();
orchestrator.ReloadDynamicPlugins(pluginsPath);

// --- 2. Initialize and Start (UI Rendering) ---
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");
AnsiConsole.MarkupLine("[grey]Tip: Press [bold white]ESC[/] during execution to cancel current task.[/]\n");

var broker = serviceProvider.GetRequiredService<IInputBroker>();
var mainCts = new CancellationTokenSource();

// Process input based on mode (Piped vs Interactive)
if (Console.IsInputRedirected)
{
    // --- Piped Input Path ---
    var cliOutput = new CliOutputHandler();
    var cliApproval = serviceProvider.GetRequiredService<IUserApprovalHandler>();
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

        var router = serviceProvider.GetRequiredService<ISmartRouter>();
        var decision = router.Route(input);
        var providerRegistry = serviceProvider.GetRequiredService<ProviderRegistry>();
        ILLMProvider provider;
        try
        {
            provider = providerRegistry.CreateProvider(decision.SelectedProvider, serviceProvider);
        }
        catch
        {
            provider = decision.SelectedProvider switch
            {
                "gemini" => serviceProvider.GetRequiredService<GeminiProvider>(),
                "gemini-cli" => serviceProvider.GetRequiredService<GeminiCliProvider>(),
                "ollama" => serviceProvider.GetRequiredService<OllamaProvider>(),
                _ => serviceProvider.GetRequiredService<ClaudeService>()
            };
        }

        var agent = serviceProvider.GetRequiredService<AgentLoop>();

        try
        {
            await agent.RunAsync(input, cliOutput, provider, decision.SelectedModel, cliApproval, mainCts.Token);
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
    // --- Interactive Mode Path (Producer-Consumer Model) ---

    // Start Discord Listener for all interactive branches
    var discordService = serviceProvider.GetRequiredService<DiscordListenerService>();
    _ = discordService.StartAsync();

    // Use LumenCliApp if requested via --lumen
    if (options.UseLumen && !options.LegacyCli)
    {
        var app = new Claude4Net.Cli.Ui.LumenCliApp(serviceProvider);
        await app.RunAsync(mainCts.Token);
    }
    else
    {
        // --- Legacy Interactive Mode Path ---

        // ESC Watcher Task
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
                        AnsiConsole.MarkupLine("\n[bold red] Cancellation requested via ESC.[/]");
                    }
                }
                Thread.Sleep(100);
            }
        });

        // [Producer] Task to capture user input
        var producerTask = Task.Run(async () =>
        {
            var cliOutput = new CliOutputHandler();
            var cliApproval = serviceProvider.GetRequiredService<IUserApprovalHandler>();
            while (!mainCts.Token.IsCancellationRequested)
            {
                try
                {
                    if (CliUserApprovalHandler.PendingApproval == null)
                        Console.Write("> ");

                    string? rawInput = Console.ReadLine();
                    if (rawInput == null)
                    {
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

                    var sb = new System.Text.StringBuilder(input);
                    Thread.Sleep(15);
                    while (Console.KeyAvailable)
                    {
                        string? nextLine = Console.ReadLine();
                        if (nextLine != null)
                        {
                            sb.AppendLine();
                            sb.Append(nextLine);
                        }
                        Thread.Sleep(15);
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

                    broker.TryWrite(new InputContext(input, cliOutput, cliApproval));
                }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[CLI]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
            }
        });

        // [Consumer] Main loop to execute AgentLoop from broker messages
        while (!mainCts.Token.IsCancellationRequested)
        {
            var agent = serviceProvider.GetRequiredService<AgentLoop>();

            try
            {
                await agent.ListenAsync(mainCts.Token);
            }
            catch (OperationCanceledException) { }
        }

        await producerTask;
    }
}

AnsiConsole.MarkupLine("[grey]Exiting main loop...[/]");
Console.Out.Flush();
Thread.Sleep(200);
return 0;

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
