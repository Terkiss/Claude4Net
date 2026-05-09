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

namespace Claude4Net.Commands
{
    /// <summary>
    /// ?�일 명령?�의 ?�의 �??�들?��? ?��??�니??
    /// </summary>
    public class Command
    {
        /// <summary> 명령???�름 (?? help, login) </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 명령?�에 ?�???�명 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 명령?��? ?�행?????�출?�는 비동�??�들??</summary>
        public Func<string, IServiceProvider, Task<string>>? Handler { get; set; }
    }

    /// <summary>
    /// Claude4Net ?�스?�에???�용 가?�한 모든 ?�용??명령?��? 관리하???��??�트리입?�다.
    /// ?�용?�의 ?�력 �?'!' ?�는 '/'�??�작?�는 명령?��? 가로채???�당 로직???�행?�니??
    /// </summary>
    public static class CommandRegistry
    {
        private static readonly List<Command> _commands = new()
        {
            // --- [?��?�?�??�스???�어] ---

            /// <summary> ?��?�??�시: ?�용 가?�한 모든 명령??목록??출력?�니?? </summary>
            new Command { Name = "help", Description = "Show help", Handler = (a, sp) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Available Commands:[/]");
                foreach(var c in _commands!.OrderBy(x => x.Name))
                {
                    sb.AppendLine($"  [bold]/{c.Name.PadRight(10)}[/] - {Markup.Escape(c.Description)}");
                }
                return Task.FromResult(sb.ToString());
            }},

            /// <summary> YOLO 모드 ?�환: 모든 보안 권한 �??�인 ?�차�??�회?�니?? </summary>
            new Command { Name = "yolo", Description = "ROOT ACCESS - Bypass all permissions", Handler = (a, sp) => {
                if (AppState.CurrentPermissionMode == PermissionMode.Yolo) {
                    AppState.CurrentPermissionMode = PermissionMode.Default;
                    return Task.FromResult("[bold green]YOLO Mode Disabled.[/] Standard permissions applied.");
                } else {
                    AppState.CurrentPermissionMode = PermissionMode.Yolo;
                    return Task.FromResult("[bold red]YOLO Mode Enabled![/] All permissions bypassed. [blink]BE CAREFUL.[/]");
                }
            }},

            /// <summary> ?�스??진단: ?�재 ?��??? ?�업 공간, ?�우???�태, DB 무결???�을 종합?�으�??��??�니?? </summary>
            new Command { Name = "doctor", Description = "Run system health check and diagnostics", Handler = async (a, sp) => {
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
                sb.AppendLine("[bold cyan]?�� Claude4Net-App Diagnostics[/]");
                sb.AppendLine(new string('-', 40));

                // 1. .NET ?��???�?OS ?�보
                sb.AppendLine($"[bold]Runtime:[/] {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                sb.AppendLine($"[bold]OS:[/] {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

                // 2. ?�업 공간 ?�태
                sb.AppendLine($"[bold]System Base Dir:[/] {Markup.Escape(AppState.SystemBaseDir)}");
                sb.AppendLine($"[bold]Current Workspace (CWD):[/] {Markup.Escape(AppState.CurrentCwd ?? "[red]NOT SET[/]")}");
                sb.AppendLine($"[bold]Permission Mode:[/] {AppState.CurrentPermissionMode}");

                // 3. ?�로바이??�??�우???�태 (SmartRouter ?�동)
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

                // 4. API ???�정 ?��? ?�인
                sb.AppendLine("[bold]API Keys Status:[/]");
                string[] providers = { "Claude", "Gemini", "Discord", "Ollama" };
                foreach(var p in providers)
                {
                    string? key = AuthManager.GetApiKey(p);
                    string status = string.IsNullOrEmpty(key) ? "[red]Missing[/]" : $"[green]Present[/] ({SourceGuard.MaskValue(key)})";
                    sb.AppendLine($"  - {p.PadRight(10)}: {status}");
                }

                // 5. TeruTeruPandas DB 무결???�인
                string dbPath = Path.Combine(AppState.SystemBaseDir, "db", "memory.db");
                bool dbExists = File.Exists(dbPath);
                sb.AppendLine($"[bold]TeruTeruPandas DB:[/] {(dbExists ? "[green]Accessible[/]" : "[yellow]Not Found[/]")}");
                if (dbExists) {
                    try {
                        var manager = PandasUniverseManager.Instance;
                        var tables = manager.TableNames.ToList();
                        sb.AppendLine($"  - Tables: {string.Join(", ", tables)}");

                        // ?�수 베이?�라???�이�?존재 ?��? ?�인
                        string[] baseline = { "agent_memory", "agent_trajectories", "audit_logs" };
                        foreach(var b in baseline)
                            if (!tables.Contains(b)) sb.AppendLine($"    [red]??Missing baseline table: {b}[/]");
                    } catch { sb.AppendLine("  - [red]Error querying database instance[/]"); }
                }

                // 6. 감사 로그 ?�약
                try {
                    await PandasUniverseManager.Instance.ExecuteAsync(u => {
                        if (u.ContainsTable("audit_logs")) {
                            var df = u.GetTableOrThrow("audit_logs");
                            sb.AppendLine($"[bold]Security Audit:[/] {df.RowCount} logs recorded");
                        }
                        return null!;
                    });
                } catch { }

                // 7. ?�러그인 로드 ?�태
                string pluginDir = Path.Combine(AppState.SystemBaseDir, "plugins");
                if (!Directory.Exists(pluginDir)) Directory.CreateDirectory(pluginDir);
                var dlls = Directory.GetFiles(pluginDir, "*.dll");
                sb.AppendLine($"[bold]Plugins:[/] {dlls.Length} loaded from {Markup.Escape(pluginDir)}");

                // 8. ?�킬 ?��??�트�??�태
                var registry = sp.GetService<SkillRegistryService>();
                if (registry != null)
                {
                    await registry.LoadAsync();
                    var sks = registry.ListSkills();
                    sb.AppendLine($"[bold]Skill Registry:[/] {sks.Count} skills discovered");
                }

                return sb.ToString();
            }},

            /// <summary> 보안 감사 로그 조회: 최근 발생??민감 ?�구 ?�행 ?�역???�인?�니?? </summary>
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

            /// <summary> ?�킬 목록 조회: ?�록??모든 ?�킬�?�??�질 지?��? ?�인?�니?? </summary>
            new Command { Name = "skills", Description = "List discovered skills and quality metrics", Handler = async (a, sp) => {
                var registry = sp.GetService<SkillRegistryService>();
                if (registry == null) return "[red]Error:[/] SkillRegistryService not available.";

                await registry.LoadAsync();
                var skills = registry.ListSkills();

                if (!skills.Any()) return "[grey]No skills registered in the current registry.[/]";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Discovered Skills:[/]");

                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("[bold]ID[/]");
                table.AddColumn("[bold]Display Name[/]");
                table.AddColumn("[bold]Version[/]");
                table.AddColumn("[bold]Success/Fail[/]");
                table.AddColumn("[bold]Avg Score[/]");

                foreach(var s in skills.OrderBy(x => x.Id))
                {
                    string scoreColor = s.Metrics.AverageScore > 0.8 ? "green" : s.Metrics.AverageScore > 0.5 ? "yellow" : "red";
                    table.AddRow(
                        Markup.Escape(s.Id),
                        Markup.Escape(s.DisplayName),
                        Markup.Escape(s.Version),
                        $"{s.Metrics.SuccessCount}/{s.Metrics.FailureCount}",
                        $"[{scoreColor}]{s.Metrics.AverageScore:P0}[/]"
                    );
                }

                AnsiConsole.Write(table);
                return $"Total {skills.Count} skills listed.";
            }},

            /// <summary> ?�킬 ?�안 목록 조회: ?�록???�킬 진화 ?�안?�을 ?�인?�니?? </summary>
            new Command { Name = "skill-proposals", Description = "List skill evolution proposals", Handler = async (a, sp) => {
                var proposalService = sp.GetService<SkillProposalService>();
                if (proposalService == null) return "[red]Error:[/] SkillProposalService not available.";

                string? ws = AppState.CurrentCwd;
                if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";

                await proposalService.LoadAsync(ws);
                var proposals = proposalService.ListProposals();

                if (!proposals.Any()) return "[grey]No skill proposals found.[/]";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Skill Evolution Proposals:[/]");

                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("[bold]ID[/]");
                table.AddColumn("[bold]Target[/]");
                table.AddColumn("[bold]Type[/]");
                table.AddColumn("[bold]Summary[/]");
                table.AddColumn("[bold]Status[/]");

                foreach(var p in proposals.OrderByDescending(x => x.CreatedAt))
                {
                    string statusColor = p.Status switch {
                        SkillProposalStatus.Approved => "green",
                        SkillProposalStatus.Rejected => "red",
                        SkillProposalStatus.Proposed => "yellow",
                        _ => "grey"
                    };
                    string target = p.SkillId ?? (p.TargetPath != null ? Path.GetFileName(p.TargetPath) : "New");
                    table.AddRow(
                        Markup.Escape(p.Id),
                        Markup.Escape(target),
                        Markup.Escape(p.Type.ToString()),
                        Markup.Escape(p.Title),
                        $"[{statusColor}]{p.Status}[/]"
                    );
                }

                AnsiConsole.Write(table);
                return $"Total {proposals.Count} proposals listed. Approving a proposal does not apply file changes.";
            }},

            /// <summary> ?�킬 ?�안 ?�성: ?�정 ?�킬???�??개선 ?�안???�성?�니?? </summary>
            new Command { Name = "skill-propose", Description = "Propose an improvement for a skill", Handler = async (a, sp) => {
                var proposalService = sp.GetService<SkillProposalService>();
                if (proposalService == null) return "[red]Error:[/] SkillProposalService not available.";

                string? ws = AppState.CurrentCwd;
                if (string.IsNullOrEmpty(ws)) return "[red]Error:[/] Workspace is not set. Use /setworkspace <path> before managing proposals.";

                var parts = a.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Usage: !skill-propose <skillId_or_path> <summary>";

                string target = parts[0];
                string summary = parts[1];

                var proposal = new SkillProposalRecord {
                    Title = summary,
                    Status = SkillProposalStatus.Proposed
                };

                if (target.Contains("/") || target.Contains("\\") || target.EndsWith(".md"))
                    proposal.TargetPath = target;
                else
                    proposal.SkillId = target;

                try {
                    await proposalService.LoadAsync(ws);
                    proposalService.CreateProposal(ws, proposal);
                    await proposalService.SaveAsync(ws);
                    return $"[green]Proposal '{proposal.Id}' created successfully.[/] Use !skill-proposals to view status.";
                } catch (Exception ex) {
                    return $"[red]Error creating proposal:[/] {ex.Message}";
                }
            }},

            // --- [?�증 �?모델 관�? ---

            /// <summary> 로그?? ?�정 ?�로바이??Claude, Gemini ????API ?��? ?�정?�거???�성?�합?�다. </summary>
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

                // 기존 ??존재 ?��? ?�인 ???�동 ?�환 (K013-3-Fix)
                if (parts.Length < 2)
                {
                    string? existingKey = AuthManager.GetApiKey(provider);
                    if (!string.IsNullOrEmpty(existingKey))
                    {
                        AppState.ActiveProvider = provider;
                        AppState.IsProviderExplicitlySet = true;
                        return $"[green]기존 ?��? ?�용?�여 {Markup.Escape(provider)}�??�환?�습?�다.[/]";
                    }
                    return $"Usage: !login <provider> <key_or_uri>\n[bold red]Error:[/] API key is required for '{Markup.Escape(provider)}'.";
                }

                await AuthManager.SaveProviderKeyAsync(provider, parts[1]);
                AppState.ActiveProvider = provider;
                AppState.IsProviderExplicitlySet = true;
                return $"[green]Logged in to {Markup.Escape(provider)}.[/] API key saved and provider switched.";
            }},

            /// <summary> 모델 변�? ?�재 ?�션?�서 ?�용??LLM 모델??검?�하거나 변경합?�다. </summary>
            new Command { Name = "model", Description = "Browse and change LLM models", Handler = async (args, sp) => {
                if (string.IsNullOrWhiteSpace(args)) {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[bold cyan]Current Session Status:[/]");
                    sb.AppendLine($"  Provider: [bold]{Markup.Escape(AppState.ActiveProvider)}[/]");
                    sb.AppendLine($"  Active Model: [bold]{Markup.Escape(AppState.ActiveModel)}[/]");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(AuthManager.GetGeminiApiKey())) {
                        sb.AppendLine("[bold yellow]Google Gemini Models (Available):[/]");
                        sb.AppendLine("  - gemini-2.0-flash, gemini-2.0-flash-lite-preview, gemini-2.0-pro-exp-02-05, gemini-2.0-flash-thinking-exp");
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

                // ?�로바이???�환 방어 로직: gemini-cli 모드??경우 모델�??�턴 매칭??무시?�고 CLI 모드 ?��?
                if (AppState.ActiveProvider == "gemini-cli")
                {
                    detectedProvider = "gemini-cli";
                }
                else
                {
                    // 모델�??�턴?�로 ?�로바이???�동 매칭
                    if (newModel.StartsWith("claude")) detectedProvider = "claude";
                    else if (newModel.StartsWith("gemini")) detectedProvider = "gemini";
                    else {
                        try {
                            var ollama = sp.GetRequiredService<OllamaProvider>();
                            var ollamaModels = await ollama.ListModelsAsync();
                            if (ollamaModels.Any(m => m.Equals(newModel, StringComparison.OrdinalIgnoreCase))) detectedProvider = "ollama";
                        } catch { }
                    }
                }

                AppState.ActiveModel = newModel;
                AppState.ActiveProvider = detectedProvider;
                AppState.IsProviderExplicitlySet = true;
                return $"[green]Model changed to:[/] [bold]{Markup.Escape(newModel)}[/] (Provider switched to: [bold]{Markup.Escape(detectedProvider)}[/])";
            }},

            // --- [?�업 공간 �??�일 ?�스??관�? ---

            /// <summary> ?�면 ?�리: 콘솔 창의 ?�용??모두 지?�니?? </summary>
            new Command { Name = "clear", Description = "Clear the console screen", Handler = (a, sp) => {
                Console.Clear();
                return Task.FromResult("[green]Console cleared.[/]");
            }},

            /// <summary> ?�일 목록: ?�재 ?�업 공간???�일 �??�더 목록???�시?�니?? </summary>
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

            /// <summary> ?�재 경로 ?�시: ?�재 ?�이?�트가 ?�치???�렉?�리 경로�?보여줍니?? </summary>
            new Command { Name = "pwd", Description = "Show current working directory", Handler = (a, sp) => {
                string currentPath = AppState.CurrentCwd ?? Environment.CurrentDirectory;
                return Task.FromResult($"[cyan]CWD:[/] {Markup.Escape(currentPath)}");
            }},

            /// <summary> ?�업 공간 ?�정: ?�이?�트??루트 ?�업 경로�??�정?�니?? (?�구 ?�행??기�??? </summary>
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

            /// <summary> ?�렉?�리 ?�동: ?�업 공간 범위 ?�에???�재 경로�?변경합?�다. </summary>
            new Command { Name = "cd", Description = "Change current working directory within workspace", Handler = (a, sp) => {
                if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Please set your workspace first using [bold]/setworkspace <path>[/]");
                if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /cd <path>");

                string combined = Path.Combine(Environment.CurrentDirectory, a);
                string newPath = Path.GetFullPath(combined);

                if (Directory.Exists(newPath)) {
                    // ?�드박스 ?�책: ?�정???�업 공간 루트 밖으�??��???것�? 금�???
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

            // --- [?�보 조회 �??�틸리티] ---

            /// <summary> ?�경 변?? ?�스?�의 ?�경 변??목록???�전?�게(마스??처리) 보여줍니?? </summary>
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

                    // 민감 ?�보 ?�동 마스??
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

            /// <summary> ?�용???�인: ?�재 로그?�된 OS ?�용??�?머신 ?�보�?출력?�니?? </summary>
            new Command { Name = "whoami", Description = "Show current user information", Handler = (a, sp) => {
                return Task.FromResult($"[cyan]User:[/] {Markup.Escape(Environment.UserName)}\n[cyan]Machine:[/] {Markup.Escape(Environment.MachineName)}\n[cyan]Domain:[/] {Markup.Escape(Environment.UserDomainName)}");
            }},

            /// <summary> ?�태 ?�인: ?�스??�??�플리�??�션???�재 ?�태(메모�? ?�레?? ?�성 ?�로바이????�??�약?�니?? </summary>
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

            /// <summary> ?�용???�인: 모델�??�큰 ?�용???�계�?보여줍니?? </summary>
            new Command { Name = "usage", Description = "Show model token usage summary", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Usage tracking is active. Summary display pending SDK update.[/]");
            }},

            /// <summary> 종료: ?�플리�??�션???�전?�게 종료?�니?? </summary>
            new Command { Name = "exit", Description = "Exit the application", Handler = (a, sp) => {
                return Task.FromResult("[bold yellow]System is shutting down... Goodbye![/]");
            }},

            /// <summary> ?�션 리셋: ?�재 LLM과의 ?�??기록??초기?�합?�다. </summary>
            new Command { Name = "reset", Description = "Reset current conversation history", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Session reset command issued. Provider history will be cleared on next turn.[/]");
            }},

            // --- [고급 코디?�이?? ---

            /// <summary> 코디?�이?? 복잡???�스?��? Planning -> Execution -> Verification ?�계별로 ?��??�트?�이?�합?�다. </summary>
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

                        // 병합 준비도(Readiness) 진행 �??�시
                        int barWidth = 20;
                        int filled = (int)(task.ReadinessScore / 100 * barWidth);
                        string bar = new string('=', filled) + new string('-', barWidth - filled);
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
                            string statusIcon = g.IsPassed ? "[green]??/]" : "[red]??/]";
                            string evidenceInfo = g.Evidences.Any() ? $" ({g.Evidences.Count} Evidence)" : (g.IsEvidenceRequired ? " [red](Evidence Required)[/]" : "");
                            sb.AppendLine($"    - {statusIcon} [bold]{g.Name}[/]: {g.Comments}{evidenceInfo}");
                            if (g.ApprovedBy != null) sb.AppendLine($"      [grey]Approved by: {g.ApprovedBy} at {g.UpdatedAt}[/]");

                            foreach(var ev in g.Evidences)
                            {
                                sb.AppendLine($"      [grey]??Evidence: {ev.Summary} (by {ev.Author})[/]");
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
        /// ?�록??모든 명령??목록??가?�옵?�다.
        /// </summary>
        public static List<Command> GetCommands() => new(_commands);

        /// <summary>
        /// ?�록??명령?�의 개수�?반환?�니??
        /// </summary>
        public static int GetCommandCount() => _commands.Count;

        /// <summary>
        /// 명령???�름?�로 ?�정 명령?��? 검?�합?�다. (?�두??'!' ?�는 '/' ?�외 ??비교)
        /// </summary>
        public static Command? FindCommand(string name) => _commands.Find(c => c.Name.Equals(name.TrimStart('!', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
