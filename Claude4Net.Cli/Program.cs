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
using System.Threading;

// ============================================================================
// Claude4Net CLI Entry Point
// ============================================================================

// --- 1. 의존성 주입 (Dependency Injection) 구성 ---
// 애플리케이션에 필요한 전역 상태 및 서비스들을 DI 컨테이너에 등록합니다.

AppState.LoadDiscordApprovers(); // 디스코드 승인자 목록 로드
var services = new ServiceCollection();

// HTTP 클라이언트 팩토리 등록 (Anthropic, Gemini, Ollama 등의 API 통신에 사용)
services.AddHttpClient();

// [Messaging] 사용자 입력 및 에이전트 간의 통신을 중계하는 브로커 등록
services.AddSingleton<IInputBroker, ChannelBroker>();

// [Discord] 디스코드 봇 연동 및 게이트웨이 이벤트 처리를 위한 서비스 등록
services.AddSingleton<DiscordListenerService>();

// [Tools] 에이전트가 사용할 수 있는 기본 도구들을 싱글톤으로 등록
services.AddSingleton<LspClient>();
services.AddSingleton<ITool, LspTool>();       // 언어 서버 프로토콜 도구
services.AddSingleton<ITool, BashTool>();      // 셸 명령 실행 도구
services.AddSingleton<ITool, FileReadTool>();  // 파일 읽기 도구
services.AddSingleton<ITool, FileWriteTool>(); // 파일 쓰기 도구
services.AddSingleton<ITool, FileEditTool>();  // 파일 수정 도구
services.AddSingleton<ITool, LsTool>();        // 디렉토리 목록 조회 도구

// [Runtime] 에이전트 실행 및 도구 관리를 위한 핵심 컴포넌트 등록
services.AddSingleton<ISmartRouter, SmartRouter>(); // 프롬프트에 따른 LLM 라우팅 엔진
services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>(); // CLI 기반 사용자 승인 핸들러

// [Skill Registry] 스킬 발견 및 품질 추적을 위한 서비스 등록
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

// [LLM Providers] 다양한 LLM 제공자(Provider)들을 DI에 등록
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
    httpClient.Timeout = TimeSpan.FromSeconds(180); // Gemini API 타임아웃 3분 설정
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
    httpClient.Timeout = TimeSpan.FromSeconds(300); // Ollama(로컬) 타임아웃 5분 설정
    return new OllamaProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
});

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

// --- 연기 테스트(Smoke Test) 경로 ---
// 자동화된 환경에서 실행 여부를 확인하기 위한 간단한 종료 테스트
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

// 동적 플러그인 로드: 지정된 'plugins' 폴더 내의 DLL들을 메모리에 로드하여 도구 확장
string pluginsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
var orchestrator = serviceProvider.GetRequiredService<ToolOrchestrator>();
orchestrator.ReloadDynamicPlugins(pluginsPath);

// --- 2. 초기화 및 시작 (UI 렌더링) ---
// Spectre.Console을 사용하여 화려한 부팅 로그를 출력합니다.
AnsiConsole.Write(new FigletText("Claude4Net").Color(Color.Orange1));
AnsiConsole.MarkupLine("[bold red]YOLO Mode Support Enabled.[/] Use [bold]!yolo[/] for root access.");
AnsiConsole.MarkupLine("[grey]Tip: Press [bold white]ESC[/] during execution to cancel current task.[/]\n");

var broker = serviceProvider.GetRequiredService<IInputBroker>();
var mainCts = new CancellationTokenSource();

// 입력 방식에 따른 처리 로직 (파이프 입력 vs 대화형 터미널)
if (Console.IsInputRedirected)
{
    // --- 파이프 입력 경로 (Piped Input) ---
    // 다른 프로세스로부터 데이터를 전달받을 때 사용되는 스트리밍 방식
    var cliOutput = new CliOutputHandler();
    var cliApproval = serviceProvider.GetRequiredService<IUserApprovalHandler>();
    string? rawLine;
    while (!mainCts.Token.IsCancellationRequested && (rawLine = Console.ReadLine()) != null)
    {
        string input = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(input)) continue;

        // 명령 처리 (! 또는 / 로 시작하는 경우)
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

        // 일반 프롬프트를 AgentLoop를 통해 처리
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
    // --- 대화형 터미널 경로 (Producer-Consumer 모델) ---

    // [보조 작업] ESC 키 감시: 실행 중인 작업을 즉시 취소할 수 있도록 별도 태스크로 실행
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

    // 디스코드 리스너 시작 (백그라운드에서 게이트웨이 이벤트 수신)
    var discordService = serviceProvider.GetRequiredService<DiscordListenerService>();
    _ = discordService.StartAsync();

    // [Producer] 사용자 입력을 읽어와 브로커에 기록하는 태스크
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

                // 사용자 승인 대기 중인 경우 (Tool 사용 승인 등)
                if (CliUserApprovalHandler.PendingApproval != null)
                {
                    var tcs = CliUserApprovalHandler.PendingApproval;
                    CliUserApprovalHandler.PendingApproval = null;
                    tcs.TrySetResult(input);
                    continue;
                }

                // 붙여넣기(Paste)로 인한 멀티라인 폭탄 방어 로직:
                // 빠른 시간 내에 다량의 입력이 들어올 경우 이를 하나의 덩어리로 묶어 처리합니다.
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

                // 명령 핸들러 실행
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

                // 입력을 브로커에 써서 Consumer(AgentLoop)가 가져가게 함
                broker.TryWrite(new InputContext(input, cliOutput, cliApproval));
            }
            catch (Exception ex)
            {
                AnsiConsole.Console.Write(new Markup($"[bold red][[CLI]] Error:[/] {Markup.Escape(ex.Message)}\n"));
            }
        }
    });

    // [Consumer] 브로커로부터 입력을 수신하여 에이전트 루프를 실행하는 메인 루프
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
            // 브로커에서 메시지가 올 때까지 대기하며 에이전트 작업 수행
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



// --- 3. 헬퍼 클래스 ---

/// <summary>
/// CLI 환경에서의 출력을 처리하는 핸들러입니다.
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
    /// 파일을 사용자에게 전송(안내)합니다.
    /// </summary>
    public Task SendFileAsync(string filePath, string? text = null)
    {
        if (!string.IsNullOrEmpty(text))
            AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] {Markup.Escape(text)}\n"));

        AnsiConsole.Console.Write(new Markup($"[bold blue][[CLI]][/] File available at: [underlined]{Markup.Escape(filePath)}[/]\n"));
        return Task.CompletedTask;
    }
}
