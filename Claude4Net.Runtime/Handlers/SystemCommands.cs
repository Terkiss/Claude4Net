using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;
using Claude4Net.Api;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using System.Diagnostics;
using System.Text.Json;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime.Handlers
{
    public static class SystemCommands
    {
        private const string LiteralApiKeyDeprecationWarning = "--api-key is deprecated; use --api-key-env <NAME>.";

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
            var apiServer = sp.GetService<Claude4Net.Runtime.ApiServer.Claude4NetApiServer>();
            if (apiServer == null)
            {
                return "[red]API Server service is not registered in runtime.[/]";
            }

            string[] parts = (a ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string action = parts.Length > 0 ? parts[0].ToLowerInvariant() : "status";

            switch (action)
            {
                case "on":
                case "start":
                    if (apiServer.IsRunning)
                    {
                        return $"[yellow]API Server is already running on[/] [cyan]{apiServer.Url}[/] (Key: [cyan][redacted][/])";
                    }

                    ApiStartOptionsParseResult parsedStart = ParseApiStartOptions(parts);
                    await apiServer.StartAsync(parsedStart.Options);
                    string displayKey = apiServer.TakeApiKeyForDisplay() ?? "[redacted]";
                    string warning = parsedStart.Warning == null
                        ? string.Empty
                        : $"[yellow]Warning: {Markup.Escape(parsedStart.Warning)}[/]\n";
                    return warning +
                           $"[bold green]✓ In-Process OpenAI API Server started on[/] [cyan]{apiServer.Url}[/]\n" +
                           $"[grey]Bearer Auth Key:[/] [cyan]{Markup.Escape(displayKey)}[/]\n" +
                           $"[grey]Available Endpoints:[/]\n" +
                           $" • [green]GET[/]  /v1/models\n" +
                           $" • [green]POST[/] /v1/chat/completions (SSE stream, JSON & Tools/Function Calling)\n" +
                           $" • [green]POST[/] /v1/embeddings (native provider dimensions)\n" +
                           $" • [green]GET[/]  /api/v1/status\n" +
                           $" • [green]GET[/]  /api/v1/usage\n" +
                           $" • [green]GET[/]  /api/v1/tools\n" +
                           $" • [green]GET[/]  /api/v1/skills";

                case "off":
                case "stop":
                    if (!apiServer.IsRunning)
                    {
                        return "[yellow]API Server is not currently running.[/]";
                    }

                    await apiServer.StopAsync();
                    return "[bold yellow]In-Process OpenAI API Server has been stopped.[/]";

                case "status":
                default:
                    if (apiServer.IsRunning)
                    {
                        return $"[bold green]API Server Status: RUNNING[/] on [cyan]{apiServer.Url}[/]\n" +
                               $"Bearer Auth Key: [cyan][redacted][/]\n" +
                               $"Active Provider: [cyan]{AppState.ActiveProvider}[/], Model: [cyan]{AppState.ActiveModel}[/]";
                    }
                    else
                    {
                        return "[bold grey]API Server Status: STOPPED[/]. Use [cyan]/api on [port] [apiKey][/] to start.";
                    }
            }
        }

        private static ApiStartOptionsParseResult ParseApiStartOptions(string[] parts)
        {
            var options = new Claude4Net.Runtime.ApiServer.Claude4NetApiServerOptions();
            var positional = new List<string>();
            bool literalApiKeySupplied = false;

            for (int index = 1; index < parts.Length; index++)
            {
                string argument = parts[index];
                switch (argument.ToLowerInvariant())
                {
                    case "--api-bind":
                    case "--bind":
                        if (index + 1 < parts.Length) options.BindAddress = parts[++index];
                        break;
                    case "--api-allow-remote":
                    case "--allow-remote":
                        options.AllowRemote = true;
                        break;
                    case "--api-certificate":
                    case "--certificate":
                        if (index + 1 < parts.Length) options.CertificatePath = parts[++index];
                        break;
                    case "--api-certificate-password-env":
                    case "--certificate-password-env":
                        if (index + 1 < parts.Length) options.CertificatePasswordEnvironmentVariable = parts[++index];
                        break;
                    case "--api-certificate-password":
                    case "--certificate-password":
                        throw new ArgumentException("Literal certificate passwords are not accepted. Use --certificate-password-env.");
                    case "--api-port":
                    case "--port":
                        if (index + 1 < parts.Length && int.TryParse(parts[++index], out int namedPort)) options.Port = namedPort;
                        break;
                    case "--api-key":
                    case "--key":
                        if (index + 1 < parts.Length)
                        {
                            options.ApiKey = parts[++index];
                            literalApiKeySupplied = true;
                        }
                        break;
                    case "--api-timeout":
                    case "--timeout":
                        if (index + 1 < parts.Length && int.TryParse(parts[++index], out int timeoutSeconds))
                        {
                            options.RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                        }
                        break;
                    case "--api-key-env":
                        if (index + 1 >= parts.Length)
                            throw new ArgumentException("--api-key-env requires an environment variable name.");
                        options.ApiKey = Environment.GetEnvironmentVariable(parts[++index]);
                        if (string.IsNullOrWhiteSpace(options.ApiKey))
                            throw new ArgumentException("The environment variable specified by --api-key-env is not set or is empty.");
                        break;
                    default:
                        if (argument.StartsWith("-", StringComparison.Ordinal))
                            throw new ArgumentException($"Unknown API option: {argument}.");
                        positional.Add(argument);
                        break;
                }
            }

            if (positional.Count > 0 && int.TryParse(positional[0], out int positionalPort))
            {
                options.Port = positionalPort;
                if (positional.Count > 1)
                {
                    options.ApiKey = positional[1];
                    literalApiKeySupplied = true;
                }
            }
            else if (positional.Count > 0 && options.ApiKey == null)
            {
                options.ApiKey = positional[0];
                literalApiKeySupplied = true;
            }

            return new ApiStartOptionsParseResult(
                options,
                literalApiKeySupplied ? LiteralApiKeyDeprecationWarning : null);
        }

        private sealed record ApiStartOptionsParseResult(
            Claude4Net.Runtime.ApiServer.Claude4NetApiServerOptions Options,
            string? Warning);

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
            
            sb.AppendLine("[bold cyan]System Status[/]");
            sb.AppendLine();
            sb.AppendLine($"[bold]Session ID:[/] {AppState.SessionId}");
            sb.AppendLine($"[bold]Workspace:[/] {AppState.CurrentCwd ?? "[red]Not Set[/]"}");
            sb.AppendLine($"[bold]Active Provider:[/] {AppState.ActiveProvider}");
            sb.AppendLine($"[bold]Active Model:[/] {AppState.ActiveModel}");
            sb.AppendLine($"[bold]Memory Usage:[/] {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            sb.AppendLine($"[bold]Permission Mode:[/] {AppState.CurrentPermissionMode}");
            sb.AppendLine($"[bold]YOLO Mode:[/] {(AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]")}");
            sb.AppendLine($"[bold]OS:[/] {Environment.OSVersion}");
            sb.AppendLine($"[bold]Runtime:[/] {Environment.Version}");
            sb.AppendLine($"[bold]Up Time:[/] {(DateTime.Now - proc.StartTime)}");

            var statusTable = new Table().Border(TableBorder.Rounded);
            statusTable.AddColumn("[bold cyan]Property[/]");
            statusTable.AddColumn("[bold yellow]Value[/]");
            statusTable.AddRow("Session ID", AppState.SessionId);
            statusTable.AddRow("Workspace", AppState.CurrentCwd ?? "[red]Not Set[/]");
            statusTable.AddRow("Active Provider", AppState.ActiveProvider);
            statusTable.AddRow("Active Model", AppState.ActiveModel);
            statusTable.AddRow("Memory Usage", $"{GC.GetTotalMemory(false) / 1024 / 1024} MB");
            statusTable.AddRow("Permission Mode", AppState.CurrentPermissionMode.ToString());
            statusTable.AddRow("YOLO Mode", (AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]"));
            statusTable.AddRow("OS", Environment.OSVersion.ToString());
            statusTable.AddRow("Runtime", Environment.Version.ToString());
            statusTable.AddRow("Up Time", (DateTime.Now - proc.StartTime).ToString());

            AnsiConsole.Write(new Panel(statusTable) { Header = new PanelHeader("System Status"), Border = BoxBorder.Rounded });

            if (AppState.Tasks.Any())
            {
                var taskTable = new Table().Border(TableBorder.Rounded);
                taskTable.AddColumn("[bold cyan]Task ID[/]");
                taskTable.AddColumn("[bold yellow]Status[/]");
                taskTable.AddColumn("[bold green]Progress[/]");

                foreach (var task in AppState.Tasks.Values)
                {
                    string progressStr = task is CoordinateTask ctTask ? $"{ctTask.ReadinessScore:0}%" : "N/A";
                    taskTable.AddRow(task.Id, task.Status, progressStr);
                }
                AnsiConsole.Write(new Panel(taskTable) { Header = new PanelHeader("Active Tasks"), Border = BoxBorder.Rounded });
            }

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

        public static Task<string> HandleTools(string a, IServiceProvider sp)
        {
            var orchestrator = sp.GetRequiredService<ToolOrchestrator>();
            var tools = orchestrator.GetTools();
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("[bold cyan]Tool Name[/]");
            table.AddColumn("[bold yellow]Description[/]");

            foreach (var tool in tools.OrderBy(t => t.Name))
            {
                table.AddRow(Markup.Escape(tool.Name), Markup.Escape(tool.Description ?? "No description"));
            }
            AnsiConsole.Write(table);
            return Task.FromResult($"Loaded tools: {string.Join(", ", tools.Select(t => t.Name))}");
        }

        public static Task<string> HandleReload(string a, IServiceProvider sp)
        {
            string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
            var orchestrator = sp.GetRequiredService<ToolOrchestrator>();
            orchestrator.ReloadDynamicPlugins(pluginsDir);

            return Task.FromResult($"[bold green]Dynamic plugins have been fully hot-reloaded from RAM! ({pluginsDir})[/]");
        }

        public static async Task<string> HandlePrune(string a, IServiceProvider sp)
        {
            int days = 7;
            if (!string.IsNullOrWhiteSpace(a) && int.TryParse(a.Trim(), out int d)) days = d;
            
            await sp.GetRequiredService<ISelfHealingService>().PruneTrajectoriesAsync(days);
            return $"Trajectory pruning completed (Retention: {days} days).";
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
