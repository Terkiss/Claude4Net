using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;
using Claude4Net.Api;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using TeruTeruPandas.Core;

namespace Claude4Net.Commands.Handlers
{
    public static class SystemCommands
    {
        public static async Task<string> HandleUsage(string a, IServiceProvider sp)
        {
            string sessionId = AppState.SessionId;
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return "[yellow]No active session found.[/]";
            }

            string ws = AppState.CurrentCwd ?? AppState.OriginalCwd ?? AppState.SystemBaseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            var eventStore = new FileAgentEventStore(ws);
            var projectionEngine = new EventProjectionEngine(eventStore);
            var usageProjection = new UsageProjection();
            projectionEngine.RegisterProjection(usageProjection);
            try
            {
                await projectionEngine.RebuildAsync(sessionId);
            }
            catch (Exception ex)
            {
                return $"[red]Failed to load event log:[/] {Markup.Escape(ex.Message)}";
            }

            var model = usageProjection.Model;

            var table = new Table().Border(TableBorder.Rounded);
            table.Title($"[bold cyan]API Token Usage & Metrics (Session: {sessionId})[/]");

            table.AddColumn("[bold]Provider[/]");
            table.AddColumn("[bold]Model[/]");
            table.AddColumn("[bold]Calls[/]");
            table.AddColumn("[bold]Input Tokens[/]");
            table.AddColumn("[bold]Output Tokens[/]");
            table.AddColumn("[bold]Latency EMA[/]");
            table.AddColumn("[bold]Accumulated Cost[/]");

            foreach (var metric in model.ModelMetrics.Values)
            {
                table.AddRow(
                    Markup.Escape(metric.Provider),
                    Markup.Escape(metric.Model),
                    metric.CallCount.ToString(),
                    metric.InputTokens.ToString("N0"),
                    metric.OutputTokens.ToString("N0"),
                    $"{metric.LatencyEma:F1} ms",
                    $"${metric.AccumulatedCost:F5}"
                );
            }

            table.AddRow(
                "[bold]TOTAL[/]",
                "",
                $"[bold]{model.TotalCalls}[/]",
                $"[bold]{model.TotalInputTokens:N0}[/]",
                $"[bold]{model.TotalOutputTokens:N0}[/]",
                $"[bold]{model.LatencyEma:F1} ms[/]",
                $"[bold]${model.TotalCost:F5}[/]"
            );

            AnsiConsole.Write(table);

            // --- Active Context Window Breakdown ---
            var providerRegistry = sp.GetService<ProviderRegistry>();
            ILLMProvider? activeProvider = null;
            if (providerRegistry != null && !string.IsNullOrWhiteSpace(AppState.ActiveProvider))
            {
                try { activeProvider = providerRegistry.CreateProvider(AppState.ActiveProvider, sp); } catch { }
            }
            int limit = activeProvider?.ContextLimit ?? (AppState.ActiveModel.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                ? Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit(AppState.ActiveModel)
                : 200000);
            int historyTokens = 0;
            int historyCount = 0;

            if (activeProvider != null)
            {
                var history = activeProvider.GetHistory()?.ToList() ?? new List<object>();
                historyCount = history.Count;
                historyTokens = activeProvider.TokenCounter.CountTokens(history);
            }

            int estimatedSystemTokens = 3500;
            int estimatedToolTokens = 3200;
            int totalActiveContext = historyTokens + estimatedSystemTokens + estimatedToolTokens;
            double percent = limit > 0 ? Math.Min(100.0, (double)totalActiveContext / limit * 100.0) : 0.0;
            int filledBars = Math.Clamp((int)(percent / 5.0), 0, 20);
            string gauge = new string('█', filledBars) + new string('░', 20 - filledBars);
            string gaugeColor = percent > 80.0 ? "red" : (percent > 60.0 ? "yellow" : "green");

            var contextPanel = new Panel(
                new Markup(
                    $"• [bold]Active Model:[/] [cyan]{Markup.Escape(AppState.ActiveModel)}[/] (Max Limit: [cyan]{limit:N0}[/] tokens)\n" +
                    $"• [bold]Current Context:[/] [bold {gaugeColor}]{totalActiveContext:N0}[/] / {limit:N0} tokens ({percent:F1}%)\n" +
                    $"• [bold]Context Gauge:[/] [{gaugeColor}][[{gauge}]][/] {percent:F1}%\n" +
                    $"• [bold]Compression Threshold:[/] {limit * 0.8:N0} tokens (Auto-compression triggers at 80%)\n\n" +
                    $"[bold underline]Context Components Breakdown:[/]\n" +
                    $"  - Conversation Turns ({historyCount} msgs): [cyan]{historyTokens:N0}[/] tokens\n" +
                    $"  - System Instructions & Rules: ~[cyan]{estimatedSystemTokens:N0}[/] tokens\n" +
                    $"  - Registered Tool Schemas: ~[cyan]{estimatedToolTokens:N0}[/] tokens\n" +
                    $"  - Free Headroom Remaining: [green]{Math.Max(0, limit - totalActiveContext):N0}[/] tokens"
                )
            )
            {
                Header = new PanelHeader("[bold yellow]🧠 Live Context Window Status[/]"),
                Border = BoxBorder.Rounded
            };

            AnsiConsole.Write(contextPanel);

            return $"Total Calls: {model.TotalCalls}, Input Tokens: {model.TotalInputTokens:N0}, Output Tokens: {model.TotalOutputTokens:N0}, Context: {totalActiveContext:N0}/{limit:N0} ({percent:F1}%)";
        }

        public static async Task<string> HandleApi(string a, IServiceProvider sp)
        {
            return await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi(a, sp);
        }

        public static Task<string> HandleHelp(string a, IServiceProvider sp)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]");
            sb.AppendLine("[bold cyan]                 🎀 Claude4Net CLI & 터미널 UI 사용 도움말                    [/]");
            sb.AppendLine("[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]\n");

            sb.AppendLine("[bold yellow]Available Commands (사용 가능한 슬래시/느낌표 명령어):[/]");
            foreach (var c in CommandRegistry.GetCommands().OrderBy(x => x.Name))
            {
                sb.AppendLine($"  [bold green]/{c.Name.PadRight(14)}[/] - {Markup.Escape(c.Description)}");
            }

            sb.AppendLine("\n[bold yellow]2. Lumen 인터랙티브 터미널 UI (기본 실행 모드):[/]");
            sb.AppendLine("  • [bold white]실행 방법:[/] [green]Claude4Net.Cli.exe[/] (Lumen TUI가 기본값으로 자동 실행됩니다)");
            sb.AppendLine("  • [bold white]주요 기능:[/] 실시간 상/하단 분할 뷰(대화 스크롤 영역 + 프롬프트 입력기), 실시간 사고(Thought) 셀, 인라인 보안 결재 팝업");
            sb.AppendLine("  • [bold white]터미널 단축키:[/] ");
            sb.AppendLine("    - [cyan]ESC[/]         : 현재 진행 중인 AI 생성 또는 도구 실행 작업 즉시 취소");
            sb.AppendLine("    - [cyan]Ctrl + L[/]    : 터미널 콘솔 화면 지우기");
            sb.AppendLine("    - [cyan]Ctrl + C[/]    : 현재 입력 중인 프롬프트 취소 및 초기화");
            sb.AppendLine("    - [cyan]PgUp / PgDn[/] : 대화 및 도구 실행 히스토리 뷰포트 스크롤");
            sb.AppendLine("    - [cyan]Up / Down[/]   : 이전 명령어 히스토리 탐색 및 멀티라인 커서 이동");

            sb.AppendLine("\n[bold yellow]3. 레거시 클래식 CLI & 스크립트 자동화 모드 (--legacy-cli):[/]");
            sb.AppendLine("  • [bold white]실행 방법:[/] [green]Claude4Net.Cli.exe --legacy-cli[/] (또는 파이프 표준 입력 연결 시)");
            sb.AppendLine("  • [bold white]주요 기능:[/] 표준 순차 스트림 REPL (> 프롬프트), CI/CD 파이프라인 및 쉘 스크립트 연동 최적화");
            sb.AppendLine("  • [bold white]전체 CLI 시작 플래그 및 옵션:[/] ");
            sb.AppendLine("    - [cyan]--legacy-cli[/]              : 레거시 표준 스트림 REPL 모드로 실행");
            sb.AppendLine("    - [cyan]--lumen[/]                   : Lumen 프레임 인터랙티브 TUI 모드로 실행 (기본값)");
            sb.AppendLine("    - [cyan]--no-dashboard[/]            : 백그라운드 웹 관제 대시보드(http://localhost:5000) 비활성화");
            sb.AppendLine("    - [cyan]--provider <이름>[/]         : 시작 LLM 프로바이더 지정 (qwen, alibaba, claude, gemini, ollama, glm 등)");
            sb.AppendLine("    - [cyan]--model <이름>[/]            : 시작 LLM 모델 지정 (예: qwen3.8-max, qwen3.6-flash, claude-3-7-sonnet)");
            sb.AppendLine("    - [cyan]--yolo[/]                    : YOLO 루트 권한 모드 활성화 (모든 보안 결재 검사 우회)");
            sb.AppendLine("    - [cyan]--permission-mode <모드>[/]  : 권한 모드 설정 (ReadOnly, WorkspaceWrite, Prompt, Yolo)");
            sb.AppendLine("    - [cyan]--api [on|off][/]            : 인프로세스 OpenAI 호환 API 서버 구동 (기본 포트: 7836)");
            sb.AppendLine("    - [cyan]--api-port <포트>[/]         : API 서버 포트 번호 지정");
            sb.AppendLine("    - [cyan]--api-key-env <환경변수명>[/]: API 서버 인증 토큰이 담긴 환경 변수 이름 지정");
            sb.AppendLine("    - [cyan]--api-timeout <초>[/]        : API 서버 요청 타임아웃 지정 (기본값: 600초)");
            sb.AppendLine("    - [cyan]--setworkspace <경로>[/]     : 시작 프로젝트 루트 작업 공간 경로 지정");
            sb.AppendLine("    - [cyan]doctor [json][/]             : 전체 시스템, 프로바이더 및 환경 상태 진단 실행");
            sb.AppendLine("    - [cyan]--smoke-exit[/]              : 스모크 시작 점검만 수행 후 즉시 종료");

            sb.AppendLine("\n[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]");
            return Task.FromResult(sb.ToString());
        }

        public static Task<string> HandleYolo(string a, IServiceProvider sp)
        {
            if (AppState.CurrentPermissionMode == PermissionMode.Yolo) {
                AppState.CurrentPermissionMode = PermissionMode.Default;
                return Task.FromResult("[bold green]YOLO Mode Disabled.[/] Standard permissions applied.");
            } else {
                AppState.CurrentPermissionMode = PermissionMode.Yolo;
                return Task.FromResult("[bold red]YOLO Mode Enabled![/] All permissions bypassed. [blink]BE CAREFUL.[/]");
            }
        }

        public static async Task<string> HandleDoctor(string a, IServiceProvider sp)
        {
            if (a.Contains("--output-format json", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                var jsonRouter = sp.GetService<ISmartRouter>();
                var metrics = jsonRouter?.GetMetrics().Select(m => new
                {
                    provider = m.ProviderName,
                    status = m.Status.ToString(),
                    latencyEma = m.LatencyEma,
                    accumulatedCost = m.AccumulatedCost
                }).ToList();
                string jsonDbPath = Path.Combine(AppState.SystemBaseDir, "db", "memory.db");
                string jsonPluginDir = Path.Combine(AppState.SystemBaseDir, "plugins");
                string[] providersForJson = { "Claude", "Gemini", "Discord", "Ollama" };
                var apiKeys = providersForJson.ToDictionary(p => p, p => !string.IsNullOrEmpty(AuthManager.GetApiKey(p)));

                var skillRegistry = sp.GetService<SkillRegistryService>();
                if (skillRegistry != null) await skillRegistry.LoadAsync();

                var payload = new
                {
                    schemaVersion = 1,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                    systemBaseDir = AppState.SystemBaseDir,
                    currentWorkspace = AppState.CurrentCwd,
                    permissionMode = AppState.CurrentPermissionMode.ToString(),
                    normalizedPermissionMode = PermissionEnforcer.Normalize(AppState.CurrentPermissionMode).ToString(),
                    providers = metrics ?? new(),
                    apiKeys,
                    database = new { path = jsonDbPath, exists = File.Exists(jsonDbPath) },
                    plugins = new { path = jsonPluginDir, count = Directory.Exists(jsonPluginDir) ? Directory.GetFiles(jsonPluginDir, "*.dll").Length : 0 },
                    skillRegistry = new { count = skillRegistry?.ListSkills().Count ?? 0 }
                };

                return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]🔍 Claude4Net-App Diagnostics[/]");
            sb.AppendLine(new string('-', 40));

            // 1. .NET 정보 및 OS 정보
            sb.AppendLine($"[bold]Runtime:[/] {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"[bold]OS:[/] {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

            // 2. 작업 공간 상태
            sb.AppendLine($"[bold]System Base Dir:[/] {Markup.Escape(AppState.SystemBaseDir)}");
            sb.AppendLine($"[bold]Current Workspace (CWD):[/] {Markup.Escape(AppState.CurrentCwd ?? "[red]NOT SET[/]")}");
            sb.AppendLine($"[bold]Permission Mode:[/] {AppState.CurrentPermissionMode}");

            // 3. 프로바이더 및 라우팅 상태 (SmartRouter 연동)
            sb.AppendLine("[bold]Provider & Routing Health:[/]");
            var router = sp.GetService<ISmartRouter>();
            if (router != null)
            {
                foreach(var m in router.GetMetrics())
                {
                    string statusColor = m.Status == ProviderHealthStatus.Healthy ? "green" : m.Status == ProviderHealthStatus.CircuitBroken ? "red" : "yellow";
                    sb.AppendLine($"  - [bold]{m.ProviderName.PadRight(10)}[/]: [[[{statusColor}]{m.Status}[/]]] Latency: {m.LatencyEma:F0}ms, Cost: {m.AccumulatedCost:F2}");
                }
            }
            else sb.AppendLine("  - [red]SmartRouter service not available[/]");

            // 4. API 키 설정 여부 확인
            sb.AppendLine("[bold]API Keys Status:[/]");
            string[] providers = { "Claude", "Gemini", "Discord", "Ollama" };
            foreach(var p in providers)
            {
                string? key = AuthManager.GetApiKey(p);
                string status = string.IsNullOrEmpty(key) ? "[red]Missing[/]" : $"[green]Present[/] ({SourceGuard.MaskValue(key)})";
                sb.AppendLine($"  - {p.PadRight(10)}: {status}");
            }

            // 5. TeruTeruPandas DB 무결성 확인
            string dbPath = Path.Combine(AppState.SystemBaseDir, "db", "memory.db");
            bool dbExists = File.Exists(dbPath);
            sb.AppendLine($"[bold]TeruTeruPandas DB:[/] {(dbExists ? "[green]Accessible[/]" : "[yellow]Not Found[/]")}");
            if (dbExists) {
                try {
                    var manager = PandasUniverseManager.Instance;
                    var tables = manager.TableNames.ToList();
                    sb.AppendLine($"  - Tables: {string.Join(", ", tables)}");

                    // 필수 베이스라인 테이블 존재 여부 확인
                    string[] baseline = { "agent_memory", "agent_trajectories", "audit_logs" };
                    foreach(var b in baseline)
                        if (!tables.Contains(b)) sb.AppendLine($"    [red]⚠ Missing baseline table: {b}[/]");
                } catch { sb.AppendLine("  - [red]Error querying database instance[/]"); }
            }

            // 6. 감사 로그 요약
            try {
                await PandasUniverseManager.Instance.ExecuteAsync(u => {
                    if (u.ContainsTable("audit_logs")) {
                        var df = u.GetTableOrThrow("audit_logs");
                        sb.AppendLine($"[bold]Security Audit:[/] {df.RowCount} logs recorded");
                    }
                    return null!;
                });
            } catch { }

            // 7. 플러그인 로드 상태
            string pluginDir = Path.Combine(AppState.SystemBaseDir, "plugins");
            if (!Directory.Exists(pluginDir)) Directory.CreateDirectory(pluginDir);
            var dlls = Directory.GetFiles(pluginDir, "*.dll");
            sb.AppendLine($"[bold]Plugins:[/] {dlls.Length} loaded from {Markup.Escape(pluginDir)}");

            // 8. 스킬 레지스트리 상태
            var registry = sp.GetService<SkillRegistryService>();
            if (registry != null)
            {
                await registry.LoadAsync();
                var sks = registry.ListSkills();
                sb.AppendLine($"[bold]Skill Registry:[/] {sks.Count} skills discovered");
            }

            return sb.ToString();
        }

        public static async Task<string> HandleAudit(string a, IServiceProvider sp)
        {
            return await PandasUniverseManager.Instance.ExecuteAsync(u => {
                if (!u.ContainsTable("audit_logs")) return "[yellow]Audit logs table not found.[/]";
                var df = u.GetTableOrThrow("audit_logs");
                if (df.RowCount == 0) return "[grey]No audit logs found.[/]";

                int count = 10;
                if (int.TryParse(a, out int requestedCount)) count = requestedCount;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[bold cyan]Latest {Math.Min(count, df.RowCount)} Security Audit Logs:[/]");
                int start = Math.Max(0, df.RowCount - count);
                for (int i = df.RowCount - 1; i >= start; i--)
                {
                    string ts = df["Timestamp"].GetValue(i)?.ToString() ?? "";
                    string tool = df["ToolName"].GetValue(i)?.ToString() ?? "";
                    string safety = df["SafetyResult"].GetValue(i)?.ToString() ?? "";
                    string status = df["Status"].GetValue(i)?.ToString() ?? "";
                    string color = status.Contains("Success") ? "green" : "red";
                    sb.AppendLine($"[[{ts}]] [bold]{tool.PadRight(15)}[/] Safety: {safety.PadRight(10)} Status: [{color}]{status}[/]");
                }
                return sb.ToString();
            });
        }

        public static async Task<string> HandleStatus(string a, IServiceProvider sp)
        {
            var proc = Process.GetCurrentProcess();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]System Status:[/]");
            sb.AppendLine($"  OS: {Markup.Escape(Environment.OSVersion.ToString())}");
            sb.AppendLine($"  Runtime: {Markup.Escape(Environment.Version.ToString())}");
            sb.AppendLine($"  Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            sb.AppendLine($"  Threads: {proc.Threads.Count}");
            sb.AppendLine($"  Up Time: {DateTime.Now - proc.StartTime}");
            sb.AppendLine();
            sb.AppendLine("[bold green]Application Status:[/]");
            sb.AppendLine($"  Active Provider: {Markup.Escape(AppState.ActiveProvider)}");
            sb.AppendLine($"  Active Model: {Markup.Escape(AppState.ActiveModel)}");
            sb.AppendLine($"  YOLO Mode: {(AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]")}");

            // --- K034: EventProjectionEngine Integration (Read Path) ---
            if (!string.IsNullOrEmpty(AppState.CurrentCwd) && !string.IsNullOrEmpty(AppState.SessionId))
            {
                try
                {
                    var eventStore = new FileAgentEventStore(AppState.CurrentCwd);
                    var engine = new EventProjectionEngine(eventStore);
                    engine.RegisterProjection(new SessionSummaryProjection());
                    await engine.CatchUpAsync(AppState.SessionId);

                    var summary = engine.GetProjection<SessionSummaryProjection>()?.Model;
                    if (summary != null)
                    {
                        sb.AppendLine();
                        sb.AppendLine("[bold blue]Session Projection (CQRS Read Model):[/]");
                        sb.AppendLine($"  Total Events: {summary.TotalEventCount}");
                    }
                }
                catch
                {
                }
            }
            return sb.ToString();
        }

        public static Task<string> HandleClear(string a, IServiceProvider sp)
        {
            Console.Clear();
            return Task.FromResult("[green]Console cleared.[/]");
        }

        public static Task<string> HandleWhoAmI(string a, IServiceProvider sp)
        {
            return Task.FromResult($"[cyan]User:[/] {Markup.Escape(Environment.UserName)}\n[cyan]Machine:[/] {Markup.Escape(Environment.MachineName)}\n[cyan]Domain:[/] {Markup.Escape(Environment.UserDomainName)}");
        }

        public static Task<string> HandleEnv(string a, IServiceProvider sp)
        {
            var sb = new System.Text.StringBuilder();
            bool showAll = a.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
                || a.Trim().Equals("--all", StringComparison.OrdinalIgnoreCase);
            const int defaultLimit = 20;
            var env = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .OrderBy(de => de.Key?.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToList();
            var visible = showAll ? env : env.Take(defaultLimit);

            sb.AppendLine($"[bold cyan]Environment Variables ({(showAll ? "All Values Source-Guarded" : $"Top {defaultLimit}")}):[/]");
            foreach(var de in visible)
            {
                string key = de.Key?.ToString() ?? "";
                string val = de.Value?.ToString() ?? "";
                string displayVal = SourceGuard.MaskValue(val, key);
                sb.AppendLine($"  [bold]{key.PadRight(30)}[/] = {displayVal}");
            }
            if (!showAll && env.Count > defaultLimit)
                sb.AppendLine($"[grey]... and {env.Count - defaultLimit} more. Use /env all to show all.[/]");

            return Task.FromResult(sb.ToString());
        }

        public static Task<string> HandleExit(string a, IServiceProvider sp)
        {
            return Task.FromResult("System is shutting down... Goodbye!");
        }

        public static Task<string> HandlePlan(string a, IServiceProvider sp)
        {
            DryRunEngine.IsActive = !DryRunEngine.IsActive;
            if (DryRunEngine.IsActive)
            {
                return Task.FromResult("[bold yellow]Plan/Dry-Run Mode Enabled.[/] All file/state modifications will be simulated and blocked. Run the agent to preview changes.");
            }
            else
            {
                return Task.FromResult("[bold green]Plan/Dry-Run Mode Disabled.[/] Modifications will be written to the real system.");
            }
        }
    }
}
