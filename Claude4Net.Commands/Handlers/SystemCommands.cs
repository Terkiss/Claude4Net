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
            return $"Total Calls: {model.TotalCalls}, Input Tokens: {model.TotalInputTokens}, Output Tokens: {model.TotalOutputTokens}, Total Cost: ${model.TotalCost:F5}, Latency EMA: {model.LatencyEma:F1} ms";
        }

        public static Task<string> HandleHelp(string a, IServiceProvider sp)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[bold cyan]Available Commands:[/]");
            foreach(var c in CommandRegistry.GetCommands().OrderBy(x => x.Name))
            {
                sb.AppendLine($"  [bold]/{c.Name.PadRight(10)}[/] - {Markup.Escape(c.Description)}");
            }
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

            sb.AppendLine($"[bold cyan]Environment Variables ({(showAll ? "All" : $"Top {defaultLimit}")}):[/]");
            foreach(var de in visible)
            {
                string key = de.Key?.ToString() ?? "";
                string val = de.Value?.ToString() ?? "";
                string displayVal = SourceGuard.MaskValue(val);
                sb.AppendLine($"  [bold]{key.PadRight(30)}[/] = {displayVal}");
            }
            if (!showAll && env.Count > defaultLimit)
                sb.AppendLine($"[grey]... and {env.Count - defaultLimit} more. Use 'env all' to show all.[/]");

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
