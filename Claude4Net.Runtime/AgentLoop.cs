using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Spectre.Console;
using Claude4Net.SDK;
using System.Text.Json;
using Claude4Net.Api;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly IEmbeddingProvider? _embedding;

        public AgentLoop(ToolOrchestrator orchestrator, IServiceProvider serviceProvider, IInputBroker broker, ISmartRouter router, IEmbeddingProvider? embedding = null)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
            _router = router;
            _embedding = embedding;
        }

        public async Task ListenAsync(CancellationToken ct = default)
        {
            AnsiConsole.MarkupLine("[bold cyan][[Agent]][/] Consumer loop started. Waiting for messages...");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _broker.ReadAsync(ct);
                    string finalPrompt = context.Text;

                    if (finalPrompt.Trim().ToLower() == "!reflect")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Analyzing agent_trajectories...[/]");
                        string diagnosis = await GenerateReflectionSummaryAsync();
                        if (string.IsNullOrEmpty(diagnosis))
                        {
                            AnsiConsole.MarkupLine("[red]분석할 궤적(agent_trajectories) 데이터가 없습니다.[/]");
                            await context.Output.WriteAsync("No trajectories found to reflect on.");
                            Console.Write("\n> ");
                            continue;
                        }
                        
                        // Update SELF_HEAL_GUIDE
                        SelfHealingService.Instance.UpdateGuide(diagnosis);
                        AnsiConsole.MarkupLine("[bold green]SELF_HEAL_GUIDE.md updated successfully.[/]");

                        finalPrompt = "당신의 최근 궤적 통계 진단서입니다.\n\n" + diagnosis + "\n\n이 데이터 기반 실패 통계를 바탕으로 작업 방식을 성찰 및 재설계하고 자율적으로 `Skills` 폴더 내에 마크다운 파일(예: `Skills/SKILL.md`)을 생성/업데이트하여 피드백 루프를 완성하세요. Skills 폴더가 없다면 우선 생성하십시오. 가이드라인은 반드시 마크다운 포맷으로 구체적으로 작성하세요. 업데이트 후 핵심 변경사항을 한국어로 보고하세요.";
                    }
                    else
                    {
                        // --- [Task 5.1: Intent-based Query Routing] ---
                        string? routedCommand = QueryRouter.Route(context.Text);

                        // --- [System Command Interception] ---
                        var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output, context.Approval) : context;
                        if (await HandleSystemCommand(effectiveContext, ct))
                        {
                            Console.Write("\n> ");
                            continue;
                        }
                        finalPrompt = effectiveContext.Text;
                    }

                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Conversations are blocked. Use [bold]/setworkspace <path>[/] first.");
                        await context.Output.WriteAsync("Error: Workspace is not set. Conversations are blocked. Use /setworkspace <path> first.");
                        Console.Write("\n> ");
                        continue;
                    }

                    // Smart Routing
                    var decision = _router.Route(finalPrompt);
                    ILLMProvider provider = decision.SelectedProvider switch
                    {
                        "gemini" => _serviceProvider.GetRequiredService<GeminiProvider>(),
                        "gemini-cli" => _serviceProvider.GetRequiredService<GeminiCliProvider>(),
                        "ollama" => _serviceProvider.GetRequiredService<OllamaProvider>(),
                        _ => _serviceProvider.GetRequiredService<ClaudeService>()
                    };

                    AnsiConsole.MarkupLine($"[grey]Routing:[/] [bold cyan]{decision.SelectedProvider}[/] ([italic]{decision.SelectedModel}[/]) - [grey]{decision.Reason ?? "Auto"}[/]");

                    // --- [RAG Retrieval] ---
                    string relevantContext = await RetrieveRelevantMemoriesAsync(finalPrompt);
                    if (!string.IsNullOrEmpty(relevantContext))
                    {
                        AnsiConsole.MarkupLine("[bold blue]🧠 Context Retrieved:[/] Found relevant past interactions in agent_memory.");
                    }
                    string promptWithContext = relevantContext + finalPrompt;

                    await RunAsync(promptWithContext, context.Output, provider, decision.SelectedModel, context.Approval, ct);

                    Console.Write("\n> ");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[Agent]] Consumer Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
            }
        }

        private async Task<string> RetrieveRelevantMemoriesAsync(string userPrompt)
        {
            if (_embedding == null) return "";
            
            var sw = Stopwatch.StartNew();
            float[]? targetVector = null;

            // 1. Check persistent cache (L2) via PandasUniverseManager
            targetVector = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("embedding_cache")) return null;
                var df = u.GetTableOrThrow("embedding_cache");
                for (int i = 0; i < df.RowCount; i++)
                {
                    if (df["Text"].GetValue(i)?.ToString() == userPrompt)
                    {
                        return df["Embedding"].GetValue(i) as float[];
                    }
                }
                return null;
            });

            // 2. If not in cache, call API
            if (targetVector == null)
            {
                try { targetVector = await _embedding.GetEmbeddingAsync(userPrompt); } catch { }
                
                // Save to L2 cache if successful
                if (targetVector != null && targetVector.Length > 0)
                {
                    var vec = targetVector;
                    _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                    {
                        if (!u.ContainsTable("embedding_cache")) return null!;
                        var df = u.GetTableOrThrow("embedding_cache");
                        var newRowCols = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                        {
                            ["Text"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { userPrompt }),
                            ["Embedding"] = new TeruTeruPandas.Core.Column.VectorColumn(new[] { vec }),
                            ["LastUsed"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.Now.ToString("O") })
                        };
                        var newRowDf = new DataFrame(newRowCols);
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("embedding_cache", updatedDf);
                        return null!;
                    });
                }
            }

            string result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory")) return "";
                var df = u.GetTableOrThrow("agent_memory");
                if (df.RowCount == 0) return "";

                DataFrame topMemories;

                // Only use semantic search if we have a valid target vector and the column exists
                if (targetVector != null && targetVector.Length > 0 && df.Columns.Contains("Embedding"))
                {
                    // Safety: Filter rows with consistent dimensions before SIMD operations
                    var embCol = df["Embedding"];
                    var validIndices = new List<int>();
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        if (embCol.GetValue(i) is float[] v && v.Length == targetVector.Length)
                        {
                            validIndices.Add(i);
                        }
                    }

                    if (validIndices.Count > 0)
                    {
                        var filteredDf = df.Reorder(validIndices.ToArray());
                        topMemories = filteredDf.OrderByDescendingCosineSimilarity("Embedding", targetVector).Head(3);
                    }
                    else
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                    
                    // If no good semantic matches found (all similarities <= 0), fallback to keywords
                    var topSim = topMemories.Columns.Contains("Similarity") ? (double)(topMemories["Similarity"].GetValue(0) ?? -1.0) : -1.0;
                    if (topSim <= 0)
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                }
                else
                {
                    // Fallback to Keyword Matching
                    topMemories = SearchByKeywords(df, userPrompt);
                }

                if (topMemories.RowCount == 0) return "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\n[시스템 주의: 과거 상호작용 기록 중 현재 요청과 관련된 내용이 발견되었습니다. 참고하십시오.]");
                for (int i = 0; i < topMemories.RowCount; i++)
                {
                    sb.AppendLine($"--- 기록 (인덱스: {i}) ---");
                    sb.AppendLine($"요청: {topMemories["UserPrompt"].GetValue(i)}");
                    sb.AppendLine($"대응: {topMemories["AgentResponse"].GetValue(i)}");
                }
                sb.AppendLine("--------------------------------------------------------------------------\n");
                return sb.ToString();
            });

            sw.Stop();
            if (sw.ElapsedMilliseconds > 200)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ Performance Warning:[/] RAG retrieval took {sw.ElapsedMilliseconds}ms.");
            }
            return result;
        }

        private DataFrame SearchByKeywords(DataFrame df, string userPrompt)
        {
            var keywordsStr = ExtractKeywords(userPrompt);
            if (string.IsNullOrEmpty(keywordsStr)) return df.Head(0);
            var currentKeywords = keywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var scored = new List<(int idx, int score)>();
            for (int i = 0; i < df.RowCount; i++)
            {
                var recordKeywordsStr = df.Columns.Contains("Keywords") ? df["Keywords"].GetValue(i)?.ToString() ?? "" : "";
                var recordKeywords = recordKeywordsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                int score = recordKeywords.Intersect(currentKeywords).Count();
                if (score > 0) scored.Add((i, score));
            }

            if (!scored.Any()) return df.Head(0);
            var indices = scored.OrderByDescending(x => x.score).ThenByDescending(x => x.idx).Take(3).Select(x => x.idx).ToArray();
            return df.Reorder(indices);
        }

        private async Task<string> GenerateReflectionSummaryAsync()
        {
            return await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_trajectories")) return "";
                var df = u.GetTableOrThrow("agent_trajectories");
                if (df.RowCount == 0) return "";

                int totalCount = df.RowCount;

                var toolNames = new List<string>();
                var isErrors = new List<bool>();
                var errorReasons = new List<string>();
                var categories = new List<string>();

                for (int i = 0; i < df.RowCount; i++)
                {
                    toolNames.Add(df["ToolName"].GetValue(i)?.ToString() ?? "");
                    isErrors.Add(df["IsError"].GetValue(i)?.ToString() == "True");
                    errorReasons.Add(df["ErrorReason"].GetValue(i)?.ToString() ?? "");
                    categories.Add(df.Columns.Any(c => c == "Category") ? df["Category"].GetValue(i)?.ToString() ?? "Unknown" : "Unknown");
                }

                var stats = toolNames.Distinct().Select(tn =>
                {
                    var indices = toolNames.Select((n, idx) => (n, idx)).Where(x => x.n == tn).Select(x => x.idx).ToList();
                    int total = indices.Count;
                    int fails = indices.Count(idx => isErrors[idx]);
                    return new { ToolName = tn, Total = total, Fails = fails, Rate = total > 0 ? (double)fails / total : 0 };
                }).OrderByDescending(x => x.Rate).ThenByDescending(x => x.Fails).ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("=== 지능형 통계 진단서 (DataUniverse Agent Trajectories) ===");
                sb.AppendLine($"총 툴 호출 횟수: {totalCount}");
                foreach (var s in stats)
                {
                    sb.AppendLine($"- {s.ToolName} : {s.Total}회 시도, {s.Fails}회 실패 (실패율 {s.Rate * 100:0.1}%)");
                }

                var failCategories = categories.Where(c => c != "Success" && c != "Unknown")
                                               .GroupBy(c => c)
                                               .OrderByDescending(g => g.Count());
                
                if (failCategories.Any())
                {
                    sb.AppendLine("\n실패 카테고리 분포:");
                    foreach (var c in failCategories) sb.AppendLine($" - {c.Key}: {c.Count()}회");
                }

                var topErrors = errorReasons.Where(e => !string.IsNullOrWhiteSpace(e) && e.Length > 3)
                                            .GroupBy(e => e)
                                            .OrderByDescending(g => g.Count())
                                            .Take(3);

                if (topErrors.Any())
                {
                    sb.AppendLine("\n주요 발생 에러 내용 (Top 3):");
                    foreach (var e in topErrors) sb.AppendLine($" - [{e.Count()}회 발생] {e.Key.Replace("\n", " ").Substring(0, Math.Min(150, e.Key.Length))}");
                }

                return sb.ToString();
            });
        }

        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            string text = context.Text.Trim();
            if (!(text.StartsWith("!") || text.StartsWith("/"))) return false;

            string[] parts = text.Split(' ', 2);
            string baseCmd = parts[0].TrimStart('!', '/').ToLowerInvariant();

            switch (baseCmd)
            {
                case "build":
                    AnsiConsole.MarkupLine("[bold blue]Building project...[/]");
                    // Logic to invoke dotnet build etc.
                    await context.Output.WriteAsync("Build triggered.");
                    return true;

                case "test":
                    AnsiConsole.MarkupLine("[bold blue]Running tests...[/]");
                    await context.Output.WriteAsync("Test suite execution started.");
                    return true;

                case "clean":
                    AnsiConsole.MarkupLine("[bold blue]Cleaning solution...[/]");
                    await context.Output.WriteAsync("Solution clean started.");
                    return true;

                case "clear":
                    Console.Clear();
                    AnsiConsole.MarkupLine("[bold green]Console cleared.[/]");
                    return true;

                case "exit":
                case "quit":
                    AnsiConsole.MarkupLine("[bold yellow]System is shutting down safely... Goodbye![/]");
                    await context.Output.WriteAsync("Agent is going offline.");
                    // No Environment.Exit(0) here. Rely on cancellation or top-level logic.
                    return true;

                case "!login":
                    var loginArgs = parts.Length > 1 ? parts[1].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();

                    if (loginArgs.Length == 0)
                    {
                        AnsiConsole.MarkupLine("[bold yellow]Usage:[/] !login <provider> [key]");
                        await context.Output.WriteAsync("Usage: !login <provider> [key]");
                        return true;
                    }

                    string providerName = loginArgs[0].ToLowerInvariant();

                    if (providerName == "geminicli" || providerName == "gemini-cli")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Switching provider to Gemini CLI...[/]");
                        AppState.ActiveProvider = "gemini-cli";
                        await context.Output.WriteAsync("Provider switched to Gemini CLI (gemini-cli). No API key required (OAuth handled by CLI).");
                    }
                    else
                    {
                        if (loginArgs.Length < 2)
                        {
                            AnsiConsole.MarkupLine($"[bold red]Error:[/] API key is required for provider '{providerName}'.");
                            await context.Output.WriteAsync($"Error: API key is required for provider '{providerName}'.");
                            return true;
                        }

                        string key = loginArgs[1];
                        await AuthManager.SaveProviderKeyAsync(providerName, key);

                        AppState.ActiveProvider = providerName;
                        AnsiConsole.MarkupLine($"[bold cyan]Switching provider to {providerName}...[/]");
                        await context.Output.WriteAsync($"Provider switched to {providerName} and API key has been saved.");
                    }
                    return true;

                case "!tools":
                    var tools = _orchestrator.GetTools();
                    var table = new Table().Border(TableBorder.Rounded);
                    table.AddColumn("[bold cyan]Tool Name[/]");
                    table.AddColumn("[bold yellow]Description[/]");

                    foreach (var tool in tools.OrderBy(t => t.Name))
                    {
                        table.AddRow(Markup.Escape(tool.Name), Markup.Escape(tool.Description ?? "No description"));
                    }
                    AnsiConsole.Write(table);
                    await context.Output.WriteAsync($"Loaded tools: {string.Join(", ", tools.Select(t => t.Name))}");
                    return true;

                case "!reload":
                    string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                    _orchestrator.ReloadDynamicPlugins(pluginsDir);

                    AnsiConsole.MarkupLine($"[bold green]Dynamic plugins have been fully hot-reloaded from RAM! ({pluginsDir})[/]");
                    await context.Output.WriteAsync("System plugins metadata and runtime assemblies refreshed.");
                    return true;

                case "!prune":
                    int days = 7;
                    if (parts.Length > 1 && int.TryParse(parts[1], out int d)) days = d;
                    AnsiConsole.MarkupLine($"[bold cyan]Pruning agent_trajectories older than {days} days...[/]");
                    await SelfHealingService.Instance.PruneTrajectoriesAsync(days);
                    await context.Output.WriteAsync($"Trajectory pruning completed (Retention: {days} days).");
                    return true;

                case "!status":
                    var process = Process.GetCurrentProcess();
                    long memoryUsed = GC.GetTotalMemory(false) / 1024 / 1024;

                    var grid = new Grid();
                    grid.AddColumn(new GridColumn().NoWrap());
                    grid.AddColumn(new GridColumn().Padding(2, 0, 0, 0));

                    grid.AddRow("[bold cyan]OS:[/]", Markup.Escape(Environment.OSVersion.ToString()));
                    grid.AddRow("[bold cyan]Active Provider:[/]", Markup.Escape(AppState.ActiveProvider));
                    grid.AddRow("[bold cyan]Active Model:[/]", Markup.Escape(AppState.ActiveModel));
                    grid.AddRow("[bold cyan]Memory Usage:[/]", $"{memoryUsed} MB");
                    grid.AddRow("[bold cyan]Loaded Tools:[/]", _orchestrator.GetTools().Count.ToString());
                    grid.AddRow("[bold cyan]YOLO Mode:[/]", AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]");

                    var panel = new Panel(grid)
                    {
                        Header = new PanelHeader("System Status"),
                        Border = BoxBorder.Rounded,
                        Padding = new Padding(1, 1, 1, 1)
                    };
                    AnsiConsole.Write(panel);
                    await context.Output.WriteAsync($"System Status: {AppState.ActiveProvider}/{AppState.ActiveModel}, Memory: {memoryUsed}MB");
                    return true;

                case "!save":
                    try
                    {
                        if (string.IsNullOrEmpty(AppState.CurrentCwd))
                        {
                            AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Use [bold]/setworkspace <path>[/] before saving context.");
                            await context.Output.WriteAsync("Error: Workspace is not set. Context save aborted.");
                            return true;
                        }

                        ILLMProvider provider;
                        if (AppState.ActiveProvider == "gemini") provider = _serviceProvider.GetRequiredService<GeminiProvider>();
                        else if (AppState.ActiveProvider == "ollama") provider = _serviceProvider.GetRequiredService<OllamaProvider>();
                        else provider = _serviceProvider.GetRequiredService<ClaudeService>();

                        var history = provider.GetHistory();
                        string dateStr = DateTime.Now.ToString("yyyyMMdd");
                        string fileName = $"context_{dateStr}.json";
                        string fullPath = Path.Combine(AppState.CurrentCwd, fileName);

                        string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(fullPath, json);

                        AnsiConsole.MarkupLine($"[bold green]Context saved to:[/] [underlined]{Markup.Escape(fullPath)}[/]");
                        await context.Output.WriteAsync($"Conversation context saved to {fileName}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Error saving context:[/] {Markup.Escape(ex.Message)}");
                    }
                    return true;
            }

            return false;
        }

        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, string model, IUserApprovalHandler? approval = null, CancellationToken ct = default)
        {
            string currentPrompt = userPrompt;
            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 200;

            var sw = Stopwatch.StartNew();
            bool hasError = false;
            string lastTurnResponse = "";

            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                turnCount++;
                var toolCalls = new List<ToolUseRequest>();
                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");

                    await foreach (var evt in provider.StreamQueryAsync(isFirstTurn ? currentPrompt : "Proceed based on previous tool results.", model: model, ct: ct))
                    {
                        if (evt.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(evt.Delta))
                        {
                            if (turnTextBuilder.Length == 0) Console.WriteLine();
                            Console.Write(evt.Delta);
                            turnTextBuilder.Append(evt.Delta);
                        }
                        else if (evt.Type == LLMStreamEventType.ThinkingDelta)
                        {
                            Console.Write(".");
                        }
                        else if (evt.Type == LLMStreamEventType.ToolCallStart && evt.ToolCall != null)
                        {
                            Console.Write("!");
                            toolCalls.Add(evt.ToolCall);
                        }
                        else if (evt.Type == LLMStreamEventType.Completed && evt.FinalResponse != null)
                        {
                            foreach (var tc in evt.FinalResponse.ToolCalls)
                            {
                                if (!toolCalls.Any(existing => existing.Id == tc.Id)) toolCalls.Add(tc);
                            }
                        }
                    }
                    Console.WriteLine();

                    if (turnTextBuilder.Length > 0)
                    {
                        lastTurnResponse = turnTextBuilder.ToString();
                        await output.WriteAsync(lastTurnResponse);
                    }
                }
                catch (Exception ex)
                {
                    hasError = true;
                    string errorMsg = $"Error ({provider.Name}): {ex.Message}";
                    AnsiConsole.Console.Write(new Markup($"\n[bold red]{Markup.Escape(errorMsg)}[/]\n"));
                    await output.WriteAsync(errorMsg);
                    break;
                }

                isFirstTurn = false;

                if (toolCalls.Count > 0)
                {
                    foreach (var tc in toolCalls)
                    {
                        AnsiConsole.MarkupLine($"[grey]🛠️  [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                    }

                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { }, approval, ct);

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";

                        if (!result.IsError && result.Content != null)
                        {
                            try
                            {
                                var json = JsonSerializer.Serialize(result.Content);
                                using var doc = JsonDocument.Parse(json);
                                if (doc.RootElement.TryGetProperty("savedPath", out var pathProp))
                                {
                                    string savedPath = pathProp.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(savedPath))
                                    {
                                        _ = output.SendFileAsync(savedPath, "Generated Image:");
                                    }
                                }
                            }
                            catch { }
                        }

                        if (summary.Length > 100) summary = summary.Substring(0, 97) + "...";

                        string escapedId = Markup.Escape(result.ToolUseId);
                        string escapedSummary = Markup.Escape(summary);

                        if (result.IsError)
                            AnsiConsole.MarkupLine($"  [red]✗ {escapedId}:[/] [grey]{escapedSummary}[/]");
                        else
                            AnsiConsole.MarkupLine($"  [green]✓ {escapedId}:[/] [grey]{escapedSummary}[/]");

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content?.ToString() ?? "Success", is_error = result.IsError });
                    }

                    // [데이터 진화 전략 Pipeline] Telemetry Ingestion to TeruTeruPandas
                    if (batchResults.Count > 0)
                    {
                        var telemetryList = new List<string>();
                        string timestamp = DateTime.Now.ToString("O");
                        foreach (var result in batchResults)
                        {
                            string toolName = toolCalls.FirstOrDefault(t => t.Id == result.ToolUseId)?.Name ?? "unknown_tool";
                            string errorText = result.IsError ? (result.Content?.ToString() ?? "Error") : "";
                            string category = result.IsError ? ErrorClassifier.Classify(toolName, errorText).ToString() : "Success";
                            
                            var dict = new Dictionary<string, object>
                            {
                                { "Timestamp", timestamp },
                                { "AgentId", AppState.SessionId },
                                { "ToolName", toolName },
                                { "IsError", result.IsError },
                                { "ErrorReason", errorText },
                                { "Category", category },
                                { "Payload", result.Content?.ToString() ?? "" }
                            };
                            telemetryList.Add(JsonSerializer.Serialize(dict));
                        }

                        var jsonArrayStr = "[" + string.Join(",", telemetryList) + "]";
                        // Non-blocking fire-and-forget ingestion
                        _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                        {
                            string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                            File.WriteAllText(tmpFile, jsonArrayStr);
                            try
                            {
                                var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                                if (u.ContainsTable("agent_trajectories"))
                                {
                                    var df = u.GetTableOrThrow("agent_trajectories");
                                    var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                                    u.AddOrUpdateTable("agent_trajectories", updatedDf);
                                }
                                else
                                {
                                    u.AddTable("agent_trajectories", newRowDf, "Auto-collected AI execution trajectories for self-reflection.");
                                }
                            }
                            catch (Exception ex)
                            {
                                AnsiConsole.Console.Write(new Markup($"[bold red][[Telemetry]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                            }
                            finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                            return null!;
                        });
                    }

                    // Task 3.2: Context Compression
                    var processedResults = ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
                    continue;
                }

                break;
            }

            await output.CompleteAsync(lastTurnResponse);

            sw.Stop();
            _router.UpdateMetric(provider.Name, sw.Elapsed.TotalMilliseconds, hasError);

            // [RAG Ingestion] Store interaction in agent_memory
            if (!hasError && !string.IsNullOrEmpty(lastTurnResponse))
            {
                var finalRes = lastTurnResponse;
                var keywords = ExtractKeywords(userPrompt + " " + finalRes);
                float[]? vector = null;
                if (_embedding != null)
                {
                    try { vector = await _embedding.GetEmbeddingAsync(userPrompt + " " + finalRes); } catch { }
                }
                
                _ = PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
                    var memoryRecord = new Dictionary<string, object?>
                    {
                        ["AgentId"] = AppState.SessionId,
                        ["Role"] = "assistant",
                        ["Status"] = "active",
                        ["CurrentTask"] = userPrompt.Length > 100 ? userPrompt.Substring(0, 97) + "..." : userPrompt,
                        ["SharedContext"] = "",
                        ["LastUpdated"] = DateTime.Now.ToString("O"),
                        ["SessionId"] = AppState.SessionId,
                        ["Keywords"] = keywords,
                        ["UserPrompt"] = userPrompt,
                        ["AgentResponse"] = finalRes,
                        ["Embedding"] = (vector != null && vector.Length > 0) ? vector : null
                    };
                    
                    File.WriteAllText(tmpFile, "[" + JsonSerializer.Serialize(memoryRecord) + "]");
                    try
                    {
                        var newRowDf = TeruTeruPandas.IO.JsonIO.ReadJson(tmpFile);
                        var df = u.GetTableOrThrow("agent_memory");
                        var updatedDf = TeruTeruPandas.Core.DataFrameJoinExtensions.Concat(new[] { df, newRowDf }, 0);
                        u.AddOrUpdateTable("agent_memory", updatedDf);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.Console.Write(new Markup($"[bold red][[RAG Storage]] Error:[/] {Markup.Escape(ex.Message)}\n"));
                    }
                    finally { if (File.Exists(tmpFile)) File.Delete(tmpFile); }
                    return null!;
                });
            }

            if (turnCount >= MAX_TURNS)
            {
                AnsiConsole.MarkupLine("\n[bold red]🛑 Circuit Breaker Hit![/]");
            }
        }

        private static readonly Regex KeywordRegex = new(@"\b\w{4,}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) 
        { 
            "this", "that", "there", "their", "where", "which", "could", "should", "would", "about", "above", "after", "again" 
        };

        private string ExtractKeywords(string text)
        {
            try
            {
                var words = KeywordRegex.Matches(text.ToLower())
                                 .Cast<Match>()
                                 .Select(m => m.Value)
                                 .Where(w => !StopWords.Contains(w))
                                 .GroupBy(w => w)
                                 .OrderByDescending(g => g.Count())
                                 .Take(10)
                                 .Select(g => g.Key);
                return string.Join(",", words);
            }
            catch { return ""; }
        }
    }
}
