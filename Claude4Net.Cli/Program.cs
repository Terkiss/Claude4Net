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
using System.IO;
using System.Threading;

// ============================================================================
// Claude4Net CLI Entry Point
// ============================================================================

// --- 1. ?˜ì¡´??ì£¼ì… (Dependency Injection) êµ¬ì„± ---
// ? í”Œë¦¬ì??´ì…˜???„ìš”???„ì—­ ?íƒœ ë°??œë¹„?¤ë“¤??DI ì»¨í…Œ?´ë„ˆ???±ë¡?©ë‹ˆ??

AppState.LoadDiscordApprovers(); // ?”ìŠ¤ì½”ë“œ ?¹ì¸??ëª©ë¡ ë¡œë“œ
var services = new ServiceCollection();

// HTTP ?´ë¼?´ì–¸???©í† ë¦??±ë¡ (Anthropic, Gemini, Ollama ?±ì˜ API ?µì‹ ???¬ìš©)
services.AddHttpClient();

// [Messaging] ?¬ìš©???…ë ¥ ë°??ì´?„íŠ¸ ê°„ì˜ ?µì‹ ??ì¤‘ê³„?˜ëŠ” ë¸Œë¡œì»??±ë¡
services.AddSingleton<IInputBroker, ChannelBroker>();

// [Discord] ?”ìŠ¤ì½”ë“œ ë´??°ë™ ë°?ê²Œì´?¸ì›¨???´ë²¤??ì²˜ë¦¬ë¥??„í•œ ?œë¹„???±ë¡
services.AddSingleton<DiscordListenerService>();

// [Tools] ?ì´?„íŠ¸ê°€ ?¬ìš©?????ˆëŠ” ê¸°ë³¸ ?„êµ¬?¤ì„ ?±ê??¤ìœ¼ë¡??±ë¡
services.AddSingleton<LspClient>();
services.AddSingleton<ITool, LspTool>();       // ?¸ì–´ ?œë²„ ?„ë¡œ? ì½œ ?„êµ¬
services.AddSingleton<ITool, BashTool>();      // ??ëª…ë ¹ ?¤í–‰ ?„êµ¬
services.AddSingleton<ITool, FileReadTool>();  // ?Œì¼ ?½ê¸° ?„êµ¬
services.AddSingleton<ITool, FileWriteTool>(); // ?Œì¼ ?°ê¸° ?„êµ¬
services.AddSingleton<ITool, FileEditTool>();  // ?Œì¼ ?˜ì • ?„êµ¬
services.AddSingleton<ITool, LsTool>();        // ?”ë ‰? ë¦¬ ëª©ë¡ ì¡°íšŒ ?„êµ¬

// [Runtime] ?ì´?„íŠ¸ ?¤í–‰ ë°??„êµ¬ ê´€ë¦¬ë? ?„í•œ ?µì‹¬ ì»´í¬?ŒíŠ¸ ?±ë¡
services.AddSingleton<ISmartRouter, SmartRouter>(); // ?„ë¡¬?„íŠ¸???°ë¥¸ LLM ?¼ìš°???”ì§„
services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>(); // CLI ê¸°ë°˜ ?¬ìš©???¹ì¸ ?¸ë“¤??

// [Skill Registry] ?¤í‚¬ ë°œê²¬ ë°??ˆì§ˆ ì¶”ì ???„í•œ ?œë¹„???±ë¡
services.AddSingleton<SkillRegistryService>(sp =>
{
    string ws = AppState.CurrentCwd ?? AppState.SystemBaseDir;
    return new SkillRegistryService(ws);
});

services.AddSingleton<SkillProposalService>(sp =>
{
    var registry = sp.GetRequiredService<SkillRegistryService>();
    return new SkillProposalService(registry);
});

services.AddSingleton<ToolOrchestrator>(sp => new ToolOrchestrator(
    sp.GetServices<ITool>(),
    sp.GetService<IUserApprovalHandler>(),
    sp));
services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolOrchestrator>());

// [LLM Providers] ?¤ì–‘??LLM ?œê³µ??Provider)?¤ì„ DI???±ë¡
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
    httpClient.Timeout = TimeSpan.FromSeconds(180); // Gemini API ?€?„ì•„??3ë¶??¤ì •
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
    httpClient.Timeout = TimeSpan.FromSeconds(300); // Ollama(ë¡œì»¬) ?€?„ì•„??5ë¶??¤ì •
    return new OllamaProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
});

bool startDashboard = args.Contains("--dashboard", StringComparer.OrdinalIgnoreCase);
if (startDashboard)
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

for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--permission-mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
    {
        if (TryParsePermissionMode(args[i + 1], out var parsedMode))
        {
            AppState.CurrentPermissionMode = parsedMode;
        }
        else
        {
            Console.Error.WriteLine($"Error: invalid permission mode '{args[i + 1]}'.");
            return 1;
        }
    }
}

if (args.Length > 0 && args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase))
{
    var cmd = CommandRegistry.FindCommand("/doctor");
    if (cmd?.Handler == null)
    {
        Console.Error.WriteLine("Error: /doctor command not found in Registry.");
        return 1;
    }

    var doctorArgs = string.Join(" ", args.Skip(1));
    var res = await cmd.Handler(doctorArgs, serviceProvider);
    Console.WriteLine(res);
    Console.Out.Flush();
    return 0;
}

// --- ?°ê¸° ?ŒìŠ¤??Smoke Test) ê²½ë¡œ ---
// ?ë™?”ëœ ?˜ê²½?ì„œ ?¤í–‰ ?¬ë?ë¥??•ì¸?˜ê¸° ?„í•œ ê°„ë‹¨??ì¢…ë£Œ ?ŒìŠ¤??
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

// ?™ì  ?ŒëŸ¬ê·¸ì¸ ë¡œë“œ: ì§€?•ëœ 'plugins' ?´ë” ?´ì˜ DLL?¤ì„ ë©”ëª¨ë¦¬ì— ë¡œë“œ?˜ì—¬ ?„êµ¬ ?•ì¥
string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
var orchestrator = serviceProvider.GetRequiredService<ToolOrchestrator>();
orchestrator.ReloadDynamicPlugins(pluginsPath);

// --- 2. ì´ˆê¸°??ë°??œì‘ (UI ?Œë”ë§? ---
// Spectre.Console???¬ìš©?˜ì—¬ ?”ë ¤??ë¶€??ë¡œê·¸ë¥?ì¶œë ¥?©ë‹ˆ??
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");
AnsiConsole.MarkupLine("[grey]Tip: Press [bold white]ESC[/] during execution to cancel current task.[/]\n");

var broker = serviceProvider.GetRequiredService<IInputBroker>();
var mainCts = new CancellationTokenSource();

// ?…ë ¥ ë°©ì‹???°ë¥¸ ì²˜ë¦¬ ë¡œì§ (?Œì´???…ë ¥ vs ?€?”í˜• ?°ë???
if (Console.IsInputRedirected)
{
    // --- ?Œì´???…ë ¥ ê²½ë¡œ (Piped Input) ---
    // ?¤ë¥¸ ?„ë¡œ?¸ìŠ¤ë¡œë????°ì´?°ë? ?„ë‹¬ë°›ì„ ???¬ìš©?˜ëŠ” ?¤íŠ¸ë¦¬ë° ë°©ì‹
    var cliOutput = new CliOutputHandler();
    var cliApproval = serviceProvider.GetRequiredService<IUserApprovalHandler>();
    string? rawLine;
    while (!mainCts.Token.IsCancellationRequested && (rawLine = Console.ReadLine()) != null)
    {
        string input = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(input)) continue;

        // ëª…ë ¹ ì²˜ë¦¬ (! ?ëŠ” / ë¡??œì‘?˜ëŠ” ê²½ìš°)
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

        // ?¼ë°˜ ?„ë¡¬?„íŠ¸ë¥?AgentLoopë¥??µí•´ ì²˜ë¦¬
        var router = serviceProvider.GetRequiredService<ISmartRouter>();
        var decision = router.Route(input);
        ILLMProvider provider = decision.SelectedProvider switch
        {
            "gemini" => serviceProvider.GetRequiredService<GeminiProvider>(),
            "gemini-cli" => serviceProvider.GetRequiredService<GeminiCliProvider>(),
            "ollama" => serviceProvider.GetRequiredService<OllamaProvider>(),
            _ => serviceProvider.GetRequiredService<ClaudeService>()
        };

        var broadcaster = DashboardServer.Services?.GetService<IAgentEventBroadcaster>();
        var agent = new AgentLoop(
            serviceProvider.GetRequiredService<ToolOrchestrator>(),
            serviceProvider,
            broker,
            router,
            serviceProvider.GetRequiredService<IEmbeddingProvider>(),
            broadcaster);

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
    // --- ?€?”í˜• ?°ë???ê²½ë¡œ (Producer-Consumer ëª¨ë¸) ---

    // [ë³´ì¡° ?‘ì—…] ESC ??ê°ì‹œ: ?¤í–‰ ì¤‘ì¸ ?‘ì—…??ì¦‰ì‹œ ì·¨ì†Œ?????ˆë„ë¡?ë³„ë„ ?œìŠ¤?¬ë¡œ ?¤í–‰
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
                    AnsiConsole.MarkupLine("\n[bold red]??Cancellation requested via ESC.[/]");
                }
            }
            Thread.Sleep(100);
        }
    });

    // ?”ìŠ¤ì½”ë“œ ë¦¬ìŠ¤???œì‘ (ë°±ê·¸?¼ìš´?œì—??ê²Œì´?¸ì›¨???´ë²¤???˜ì‹ )
    var discordService = serviceProvider.GetRequiredService<DiscordListenerService>();
    _ = discordService.StartAsync();

    // [Producer] ?¬ìš©???…ë ¥???½ì–´?€ ë¸Œë¡œì»¤ì— ê¸°ë¡?˜ëŠ” ?œìŠ¤??
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

                // ?¬ìš©???¹ì¸ ?€ê¸?ì¤‘ì¸ ê²½ìš° (Tool ?¬ìš© ?¹ì¸ ??
                if (CliUserApprovalHandler.PendingApproval != null)
                {
                    var tcs = CliUserApprovalHandler.PendingApproval;
                    CliUserApprovalHandler.PendingApproval = null;
                    tcs.TrySetResult(input);
                    continue;
                }

                // ë¶™ì—¬?£ê¸°(Paste)ë¡??¸í•œ ë©€?°ë¼????ƒ„ ë°©ì–´ ë¡œì§:
                // ë¹ ë¥¸ ?œê°„ ?´ì— ?¤ëŸ‰???…ë ¥???¤ì–´??ê²½ìš° ?´ë? ?˜ë‚˜???©ì–´ë¦¬ë¡œ ë¬¶ì–´ ì²˜ë¦¬?©ë‹ˆ??
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

                // ëª…ë ¹ ?¸ë“¤???¤í–‰
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

                // ?…ë ¥??ë¸Œë¡œì»¤ì— ?¨ì„œ Consumer(AgentLoop)ê°€ ê°€?¸ê?ê²???
                broker.TryWrite(new InputContext(input, cliOutput, cliApproval));
            }
            catch (Exception ex)
            {
                AnsiConsole.Console.Write(new Markup($"[bold red][[CLI]] Error:[/] {Markup.Escape(ex.Message)}\n"));
            }
        }
    });

    // [Consumer] ë¸Œë¡œì»¤ë¡œë¶€???…ë ¥???˜ì‹ ?˜ì—¬ ?ì´?„íŠ¸ ë£¨í”„ë¥??¤í–‰?˜ëŠ” ë©”ì¸ ë£¨í”„
    while (!mainCts.Token.IsCancellationRequested)
    {
        var broadcaster = DashboardServer.Services?.GetService<IAgentEventBroadcaster>();
        var agent = new AgentLoop(
            serviceProvider.GetRequiredService<ToolOrchestrator>(),
            serviceProvider,
            broker,
            serviceProvider.GetRequiredService<ISmartRouter>(),
            serviceProvider.GetRequiredService<IEmbeddingProvider>(),
            broadcaster);

        try
        {
            // ë¸Œë¡œì»¤ì—??ë©”ì‹œì§€ê°€ ???Œê¹Œì§€ ?€ê¸°í•˜ë©??ì´?„íŠ¸ ?‘ì—… ?˜í–‰
            await agent.ListenAsync(mainCts.Token);
        }
        catch (OperationCanceledException) { }
    }

    await producerTask;
}

AnsiConsole.MarkupLine("[grey]Exiting main loop...[/]");
Console.Out.Flush();
Thread.Sleep(200);
return 0;



// --- 3. ?¬í¼ ?´ë˜??---

/// <summary>
/// CLI ?˜ê²½?ì„œ??ì¶œë ¥??ì²˜ë¦¬?˜ëŠ” ?¸ë“¤?¬ì…?ˆë‹¤.
/// </summary>
static bool TryParsePermissionMode(string raw, out PermissionMode mode)
{
    string normalized = raw.Replace("-", "", StringComparison.OrdinalIgnoreCase)
        .Replace("_", "", StringComparison.OrdinalIgnoreCase)
        .ToLowerInvariant();

    mode = normalized switch
    {
        "readonly" => PermissionMode.ReadOnly,
        "workspacewrite" => PermissionMode.WorkspaceWrite,
        "prompt" => PermissionMode.Prompt,
        "dangerfullaccess" => PermissionMode.DangerFullAccess,
        "default" => PermissionMode.Default,
        "yolo" => PermissionMode.Yolo,
        "bypasspermissions" => PermissionMode.BypassPermissions,
        _ => default
    };

    return normalized is "readonly" or "workspacewrite" or "prompt" or "dangerfullaccess" or "default" or "yolo" or "bypasspermissions";
}

public class CliOutputHandler : IOutputHandler
{
    public Task WriteAsync(string text) => Task.CompletedTask;

    public Task CompleteAsync(string finalMessage) => Task.CompletedTask;

    /// <summary>
    /// ?Œì¼???¬ìš©?ì—ê²??„ì†¡(?ˆë‚´)?©ë‹ˆ??
    /// </summary>
    public Task SendFileAsync(string filePath, string? text = null)
    {
        if (!string.IsNullOrEmpty(text))
            AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] {Markup.Escape(text)}\n"));

        AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] File available at: [underlined]{Markup.Escape(filePath)}[/]\n"));
        return Task.CompletedTask;
    }
}
