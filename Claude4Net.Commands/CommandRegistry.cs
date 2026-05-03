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
using Spectre.Console;

namespace Claude4Net.Commands
{
    /// <summary>
    /// 단일 명령어의 정의 및 핸들러를 나타냅니다.
    /// </summary>
    public class Command
    {
        /// <summary> 명령어 이름 (예: help, login) </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 명령어에 대한 설명 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 명령어가 실행될 때 호출되는 비동기 핸들러 </summary>
        public Func<string, IServiceProvider, Task<string>>? Handler { get; set; }
    }

    /// <summary>
    /// Claude4Net 시스템에서 사용 가능한 모든 사용자 명령어를 관리하는 레지스트리입니다.
    /// 사용자의 입력 중 '!' 또는 '/'로 시작하는 명령어를 가로채어 해당 로직을 실행합니다.
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly List<Command> _commands = new()
        {
            // --- [도움말 및 시스템 제어] ---
            
            /// <summary> 도움말 표시: 사용 가능한 모든 명령어 목록을 출력합니다. </summary>
            new Command { Name = "help", Description = "Show help", Handler = (a, sp) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Available Commands:[/]");
                foreach(var c in _commands!.OrderBy(x => x.Name))
                {
                    sb.AppendLine($"  [bold]/{c.Name.PadRight(10)}[/] - {Markup.Escape(c.Description)}");
                }
                return Task.FromResult(sb.ToString());
            }},
            
            /// <summary> YOLO 모드 전환: 모든 보안 권한 및 승인 절차를 우회합니다. </summary>
            new Command { Name = "yolo", Description = "ROOT ACCESS - Bypass all permissions", Handler = (a, sp) => {
                if (AppState.CurrentPermissionMode == PermissionMode.Yolo) {
                    AppState.CurrentPermissionMode = PermissionMode.Default;
                    return Task.FromResult("[bold green]YOLO Mode Disabled.[/] Standard permissions applied.");
                } else {
                    AppState.CurrentPermissionMode = PermissionMode.Yolo;
                    return Task.FromResult("[bold red]YOLO Mode Enabled![/] All permissions bypassed. [blink]BE CAREFUL.[/]");
                }
            }},

            /// <summary> 시스템 진단: 현재 런타임, 작업 공간, 라우팅 상태, DB 무결성 등을 종합적으로 점검합니다. </summary>
            new Command { Name = "doctor", Description = "Run system health check and diagnostics", Handler = async (a, sp) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]🩺 Claude4Net-App Diagnostics[/]");
                sb.AppendLine(new string('-', 40));

                // 1. .NET 런타임 및 OS 정보
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

                return sb.ToString();
            }},

            /// <summary> 보안 감사 로그 조회: 최근 발생한 민감 도구 실행 내역을 확인합니다. </summary>
            new Command { Name = "audit", Description = "Show recent security audit logs", Handler = async (a, sp) => {
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
            }},

            // --- [인증 및 모델 관리] ---

            /// <summary> 로그인: 특정 프로바이더(Claude, Gemini 등)의 API 키를 설정하거나 활성화합니다. </summary>
            new Command { Name = "login", Description = "Log in to a provider (gemini, claude, ollama, gemini-cli)", Handler = async (args, sp) => {
                var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "Usage: !login <provider> [key_or_uri]";
                
                string provider = parts[0].ToLowerInvariant();
                if (provider == "geminicli" || provider == "gemini-cli")
                {
                    AppState.ActiveProvider = "gemini-cli";
                    AppState.IsProviderExplicitlySet = true;
                    return $"[green]Logged in to Gemini CLI (gemini-cli).[/] No API key required (OAuth handled by CLI). Provider switched.";
                }

                if (parts.Length < 2) return $"Usage: !login <provider> <key_or_uri>\n[bold red]Error:[/] API key is required for '{Markup.Escape(provider)}'.";
                
                await AuthManager.SaveProviderKeyAsync(provider, parts[1]);
                AppState.ActiveProvider = provider;
                AppState.IsProviderExplicitlySet = true;
                return $"[green]Logged in to {Markup.Escape(provider)}.[/] API key saved and provider switched.";
            }},

            /// <summary> 모델 변경: 현재 세션에서 사용할 LLM 모델을 검색하거나 변경합니다. </summary>
            new Command { Name = "model", Description = "Browse and change LLM models", Handler = async (args, sp) => {
                if (string.IsNullOrWhiteSpace(args)) {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[bold cyan]Current Session Status:[/]");
                    sb.AppendLine($"  Provider: [bold]{Markup.Escape(AppState.ActiveProvider)}[/]");
                    sb.AppendLine($"  Active Model: [bold]{Markup.Escape(AppState.ActiveModel)}[/]");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(AuthManager.GetGeminiApiKey())) {
                        sb.AppendLine("[bold yellow]Google Gemini Models (Available):[/]");
                        sb.AppendLine("  - gemini-3.1-pro, gemini-3.0-flash, gemini-3.1-deep-think, gemini-3.1-flash-lite, gemini-3.1-flash-live");
                        sb.AppendLine("  - gemini-2.5-pro, gemini-2.5-flash");
                        sb.AppendLine("  - gemini-2.0-flash, gemini-2.0-flash-lite-preview-02-05, gemini-2.0-pro-exp-02-05, gemini-2.0-flash-thinking-exp-01-21");
                        sb.AppendLine("  - gemini-1.5-pro, gemini-1.5-flash, gemini-1.5-flash-8b");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrEmpty(AuthManager.GetAnthropicApiKey())) {
                        sb.AppendLine("[bold magenta]Anthropic Claude Models (Available):[/]");
                        sb.AppendLine("  - claude-3-5-sonnet-20241022, claude-3-5-haiku-20241022");
                        sb.AppendLine();
                    }

                    string? ollamaUri = AuthManager.GetApiKey("ollama");
                    if (!string.IsNullOrEmpty(ollamaUri)) {
                        sb.AppendLine("[bold green]Ollama Local Models (Real-time):[/]");
                        try {
                            var ollama = sp.GetRequiredService<OllamaProvider>();
                            var models = await ollama.ListModelsAsync();
                            if (models.Any()) foreach(var m in models) sb.AppendLine($"  - {Markup.Escape(m)}");
                            else sb.AppendLine("  (No local models found)");
                        } catch { sb.AppendLine("  (Ollama server not reachable)"); }
                        sb.AppendLine();
                    }

                    sb.AppendLine("[grey]To change model, type: /model <model_name>[/]");
                    return sb.ToString();
                }

                string newModel = args.Trim();
                string detectedProvider = AppState.ActiveProvider;

                // 모델명 패턴으로 프로바이더 자동 매칭
                if (newModel.StartsWith("claude")) detectedProvider = "claude";
                else if (newModel.StartsWith("gemini")) detectedProvider = "gemini";
                else {
                    try {
                        var ollama = sp.GetRequiredService<OllamaProvider>();
                        var ollamaModels = await ollama.ListModelsAsync();
                        if (ollamaModels.Any(m => m.Equals(newModel, StringComparison.OrdinalIgnoreCase))) detectedProvider = "ollama";
                    } catch { }
                }

                AppState.ActiveModel = newModel;
                AppState.ActiveProvider = detectedProvider;
                AppState.IsProviderExplicitlySet = true;
                return $"[green]Model changed to:[/] [bold]{Markup.Escape(newModel)}[/] (Provider switched to: [bold]{Markup.Escape(detectedProvider)}[/])";
            }},

            // --- [작업 공간 및 파일 시스템 관리] ---

            /// <summary> 화면 정리: 콘솔 창의 내용을 모두 지웁니다. </summary>
            new Command { Name = "clear", Description = "Clear the console screen", Handler = (a, sp) => {
                Console.Clear();
                return Task.FromResult("[green]Console cleared.[/]");
            }},

            /// <summary> 파일 목록: 현재 작업 공간의 파일 및 폴더 목록을 표시합니다. </summary>
            new Command { Name = "ls", Description = "List files in current directory", Handler = (a, sp) => {
                if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Workspace is not set. Use [bold]/setworkspace <path>[/] first.");
                
                string currentPath = Environment.CurrentDirectory;
                var files = Directory.GetFileSystemEntries(currentPath);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[bold cyan]Directory: {Markup.Escape(currentPath)}[/]");
                foreach(var f in files) 
                {
                    bool isDir = Directory.Exists(f);
                    string tag = isDir ? "[bold blue][[Dir]][/]" : "[grey][[File]][/]";
                    sb.AppendLine($"  {tag} {Markup.Escape(Path.GetFileName(f))}");
                }
                return Task.FromResult(sb.ToString());
            }},

            /// <summary> 현재 경로 표시: 현재 에이전트가 위치한 디렉토리 경로를 보여줍니다. </summary>
            new Command { Name = "pwd", Description = "Show current working directory", Handler = (a, sp) => {
                string currentPath = AppState.CurrentCwd ?? Environment.CurrentDirectory;
                return Task.FromResult($"[cyan]CWD:[/] {Markup.Escape(currentPath)}");
            }},

            /// <summary> 작업 공간 설정: 에이전트의 루트 작업 경로를 설정합니다. (도구 실행의 기준점) </summary>
            new Command { Name = "setworkspace", Description = "Set the root project workspace path (Required for tools)", Handler = (a, sp) => {
                if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /setworkspace <path>");
                string newPath = Path.GetFullPath(a);
                if (Directory.Exists(newPath)) {
                    AppState.CurrentCwd = newPath;
                    Environment.CurrentDirectory = newPath;
                    return Task.FromResult($"[bold green]Workspace set to:[/] {Markup.Escape(newPath)}\n[grey]Tools are now active for this directory.[/]");
                }
                return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
            }},

            /// <summary> 디렉토리 이동: 작업 공간 범위 내에서 현재 경로를 변경합니다. </summary>
            new Command { Name = "cd", Description = "Change current working directory within workspace", Handler = (a, sp) => {
                if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Please set your workspace first using [bold]/setworkspace <path>[/]");
                if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /cd <path>");
                
                string combined = Path.Combine(Environment.CurrentDirectory, a);
                string newPath = Path.GetFullPath(combined);
                
                if (Directory.Exists(newPath)) {
                    // 샌드박스 정책: 설정된 작업 공간 루트 밖으로 나가는 것은 금지됨
                    string normalizedWorkspace = AppState.CurrentCwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    string normalizedNewPath = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                    if (normalizedNewPath.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase)) {
                        Environment.CurrentDirectory = newPath;
                        return Task.FromResult($"[green]Directory changed to:[/] {Markup.Escape(newPath)}");
                    }
                    return Task.FromResult($"[red]Error:[/] Cannot move outside the set workspace root: {Markup.Escape(AppState.CurrentCwd)}");
                }
                return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
            }},

            // --- [정보 조회 및 유틸리티] ---

            /// <summary> 환경 변수: 시스템의 환경 변수 목록을 안전하게(마스킹 처리) 보여줍니다. </summary>
            new Command { Name = "env", Description = "List environment variables (masked, use all/--all for full output)", Handler = (a, sp) => {
                var sb = new System.Text.StringBuilder();
                bool showAll = a.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)
                    || a.Trim().Equals("--all", StringComparison.OrdinalIgnoreCase);
                const int defaultLimit = 20;
                var env = Environment.GetEnvironmentVariables()
                    .Cast<System.Collections.DictionaryEntry>()
                    .OrderBy(de => de.Key?.ToString(), StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var visible = showAll ? env : env.Take(defaultLimit);

                sb.AppendLine(showAll
                    ? "[bold cyan]Environment Variables (All Values Source-Guarded):[/]"
                    : $"[bold cyan]Environment Variables (Top {Math.Min(defaultLimit, env.Count)} of {env.Count}, Source-Guarded):[/]");
                
                foreach(System.Collections.DictionaryEntry de in visible) {
                    string key = de.Key?.ToString() ?? "Unknown";
                    string val = de.Value?.ToString() ?? "";
                    
                    // 민감 정보 자동 마스킹
                    string maskedVal = SourceGuard.MaskValue(val, key);

                    sb.AppendLine($"  [bold]{Markup.Escape(key)}[/]: {Markup.Escape(maskedVal)}");
                }

                if (!showAll)
                {
                    sb.AppendLine();
                    sb.AppendLine("  (Use /env all to see all variables)");
                }
                return Task.FromResult(sb.ToString());
            }},

            /// <summary> 사용자 확인: 현재 로그인된 OS 사용자 및 머신 정보를 출력합니다. </summary>
            new Command { Name = "whoami", Description = "Show current user information", Handler = (a, sp) => {
                return Task.FromResult($"[cyan]User:[/] {Markup.Escape(Environment.UserName)}\n[cyan]Machine:[/] {Markup.Escape(Environment.MachineName)}\n[cyan]Domain:[/] {Markup.Escape(Environment.UserDomainName)}");
            }},

            /// <summary> 상태 확인: 시스템 및 애플리케이션의 현재 상태(메모리, 스레드, 활성 프로바이더 등)를 요약합니다. </summary>
            new Command { Name = "status", Description = "Show system and application status", Handler = (a, sp) => {
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
                return Task.FromResult(sb.ToString());
            }},

            /// <summary> 사용량 확인: 모델별 토큰 사용량 통계를 보여줍니다. </summary>
            new Command { Name = "usage", Description = "Show model token usage summary", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Usage tracking is active. Summary display pending SDK update.[/]");
            }},

            /// <summary> 종료: 애플리케이션을 안전하게 종료합니다. </summary>
            new Command { Name = "exit", Description = "Exit the application", Handler = (a, sp) => {
                return Task.FromResult("[bold yellow]System is shutting down... Goodbye![/]");
            }},

            /// <summary> 세션 리셋: 현재 LLM과의 대화 기록을 초기화합니다. </summary>
            new Command { Name = "reset", Description = "Reset current conversation history", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Session reset command issued. Provider history will be cleared on next turn.[/]");
            }},

            // --- [고급 코디네이션] ---

            /// <summary> 코디네이션: 복잡한 태스크를 Planning -> Execution -> Verification 단계별로 오케스트레이션합니다. </summary>
            new Command { Name = "coordinate", Description = "Orchestrate tasks through Planning -> Execution -> Verification phases", Handler = (a, sp) => {
                var parts = a.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return Task.FromResult("Usage: /coordinate <list|start|status|phase|gate|evidence|approve|reject>");

                string sub = parts[0].ToLowerInvariant();
                var store = CoordinatorStore.Instance;

                switch (sub)
                {
                    case "list":
                        var tasks = AppState.GetCoordinatedTasks().ToList();
                        if (!tasks.Any()) return Task.FromResult("[grey]No coordinated tasks found.[/]");
                        var table = new System.Text.StringBuilder();
                        table.AppendLine("[bold cyan]Coordinated Tasks:[/]");
                        foreach(var t in tasks) 
                        {
                            string scoreColor = t.ReadinessScore > 80 ? "green" : t.ReadinessScore > 40 ? "yellow" : "red";
                            table.AppendLine($"  - [[{t.Id}]] [bold]{t.Title}[/] ({t.CurrentPhase}) [[[{scoreColor}]{t.ReadinessScore:0}%[/]]] - {t.ReviewStatus}");
                        }
                        return Task.FromResult(table.ToString());

                    case "start":
                        if (parts.Length < 3) return Task.FromResult("Usage: /coordinate start <id> <title> [description]");
                        string id = parts[1];
                        string title = parts[2];
                        string desc = parts.Length > 3 ? string.Join(" ", parts.Skip(3)) : title;
                        try {
                            store.CreateTask(id, title, desc);
                            return Task.FromResult($"[green]Task '{title}' started with ID '{id}'. Phase: Planning[/]");
                        } catch (Exception ex) {
                            return Task.FromResult($"[red]Error:[/] {ex.Message}");
                        }

                    case "status":
                        if (parts.Length < 2) return Task.FromResult("Usage: /coordinate status <id>");
                        if (!AppState.Tasks.TryGetValue(parts[1], out var st) || st is not CoordinateTask task) return Task.FromResult($"[red]Error:[/] Coordinated task '{parts[1]}' not found.");
                        
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine($"[bold cyan]Task Details: {task.Title} ({task.Id})[/]");
                        sb.AppendLine($"  [bold]Description:[/] {task.Description}");
                        sb.AppendLine($"  [bold]Phase:[/] {task.CurrentPhase}");
                        sb.AppendLine($"  [bold]Review:[/] {task.ReviewStatus}");
                        
                        // 병합 준비도(Readiness) 진행 바 표시
                        int barWidth = 20;
                        int filled = (int)(task.ReadinessScore / 100 * barWidth);
                        string bar = new string('█', filled) + new string('░', barWidth - filled);
                        string barColor = task.ReadinessScore >= 90 ? "green" : task.ReadinessScore >= 50 ? "yellow" : "blue";
                        sb.AppendLine($"  [bold]Merge Readiness:[/] [{barColor}]{bar}[/] {task.ReadinessScore:0}%");

                        if (task.Blockers.Any())
                        {
                            sb.AppendLine($"  [bold red]Blockers:[/]");
                            foreach (var b in task.Blockers) sb.AppendLine($"    - {b}");
                        }

                        sb.AppendLine($"  [bold]Gates:[/]");
                        if (!task.Gates.Any()) sb.AppendLine("    (No gates defined)");
                        foreach(var g in task.Gates) 
                        {
                            string statusIcon = g.IsPassed ? "[green]✔[/]" : "[red]✘[/]";
                            string evidenceInfo = g.Evidences.Any() ? $" ({g.Evidences.Count} Evidence)" : (g.IsEvidenceRequired ? " [red](Evidence Required)[/]" : "");
                            sb.AppendLine($"    - {statusIcon} [bold]{g.Name}[/]: {g.Comments}{evidenceInfo}");
                            if (g.ApprovedBy != null) sb.AppendLine($"      [grey]Approved by: {g.ApprovedBy} at {g.UpdatedAt}[/]");
                            
                            foreach(var ev in g.Evidences)
                            {
                                sb.AppendLine($"      [grey]└ Evidence: {ev.Summary} (by {ev.Author})[/]");
                            }
                        }
                        
                        sb.AppendLine($"  [bold]History (Last 5):[/]");
                        foreach(var h in task.History.AsEnumerable().Reverse().Take(5)) sb.AppendLine($"    - {h}");

                        return Task.FromResult(sb.ToString());

                    case "phase":
                        if (parts.Length < 3) return Task.FromResult("Usage: /coordinate phase <id> <Planning|Execution|Verification|Completed>");
                        if (Enum.TryParse<CoordinatePhase>(parts[2], true, out var newPhase)) {
                            string res = store.TransitionPhase(parts[1], newPhase);
                            return Task.FromResult(res.StartsWith("Error") ? $"[red]{res}[/]" : $"[green]{res}[/]");
                        }
                        return Task.FromResult($"[red]Error:[/] Invalid phase '{parts[2]}'.");

                    case "gate":
                        if (parts.Length < 4) return Task.FromResult("Usage: /coordinate gate <id> <name> <true|false> [comments]");
                        if (bool.TryParse(parts[3], out bool passed)) {
                            string? gComments = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : null;
                            string user = Environment.UserName;
                            string res = store.UpdateGate(parts[1], parts[2], passed, gComments, user);
                            return Task.FromResult(res.StartsWith("Error") ? $"[red]{res}[/]" : $"[green]{res}[/]");
                        }
                        return Task.FromResult($"[red]Error:[/] Invalid boolean value '{parts[3]}'.");

                    case "evidence":
                        if (parts.Length < 4) return Task.FromResult("Usage: /coordinate evidence <taskId> <gateName> <summary> [details]");
                        string evTaskId = parts[1];
                        string evGateName = parts[2];
                        string evSummary = parts[3];
                        string? evDetails = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : null;
                        string evAuthor = Environment.UserName;
                        
                        string evRes = store.AddEvidence(evTaskId, evGateName, evAuthor, evSummary, evDetails);
                        return Task.FromResult(evRes.StartsWith("Error") ? $"[red]{evRes}[/]" : $"[green]{evRes}[/]");

                    case "approve":
                        if (parts.Length < 2) return Task.FromResult("Usage: /coordinate approve <id> [comments]");
                        string aComments = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Approved via CLI";
                        string aRes = store.SetReview(parts[1], ReviewerDecision.Approved, aComments);
                        return Task.FromResult(aRes.StartsWith("Error") ? $"[red]{aRes}[/]" : $"[green]{aRes}[/]");

                    case "reject":
                        if (parts.Length < 2) return Task.FromResult("Usage: /coordinate reject <id> [comments]");
                        string rComments = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Rejected via CLI";
                        string rRes = store.SetReview(parts[1], ReviewerDecision.Rejected, rComments);
                        return Task.FromResult(rRes.StartsWith("Error") ? $"[red]{rRes}[/]" : $"[yellow]{rRes}[/]");

                    default:
                        return Task.FromResult($"[red]Unknown subcommand:[/] {sub}");
                }
            }}
        };

        /// <summary>
        /// 등록된 모든 명령어 목록을 가져옵니다.
        /// </summary>
        public static List<Command> GetCommands() => new(_commands);

        /// <summary>
        /// 등록된 명령어의 개수를 반환합니다.
        /// </summary>
        public static int GetCommandCount() => _commands.Count;

        /// <summary>
        /// 명령어 이름으로 특정 명령어를 검색합니다. (접두사 '!' 또는 '/' 제외 후 비교)
        /// </summary>
        public static Command? FindCommand(string name) => _commands.Find(c => c.Name.Equals(name.TrimStart('!', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
