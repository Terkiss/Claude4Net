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
    /// <summary>
    /// Claude4Net???듭떖 ?ㅽ뻾 ?붿쭊?쇰줈, ?먯씠?꾪듃???ш퀬-?됰룞-愿李?Reasoning Loop) 怨쇱젙??珥앷큵?⑸땲??
    /// ?낅젰 泥섎━, ?ㅻ쭏???쇱슦?? RAG 寃?? ?꾧뎄 ?ㅽ뻾 諛??먭? 移섏쑀瑜??꾪븳 沅ㅼ쟻 ?섏쭛???대떦?⑸땲??
    /// </summary>
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly IEmbeddingProvider? _embedding;
        private AgentSessionStore? _sessionStore;

        /// <summary>
        /// AgentLoop?????몄뒪?댁뒪瑜?珥덇린?뷀빀?덈떎.
        /// </summary>
        public AgentLoop(ToolOrchestrator orchestrator, IServiceProvider serviceProvider, IInputBroker broker, ISmartRouter router, IEmbeddingProvider? embedding = null)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
            _router = router;
            _embedding = embedding;
        }

        private async Task EnsureSessionInitializedAsync(string providerName, string modelName)
        {
            if (_sessionStore != null || string.IsNullOrEmpty(AppState.CurrentCwd)) return;

            _sessionStore = new AgentSessionStore(AppState.CurrentCwd, AppState.SessionId);
            var record = new AgentSessionRecord
            {
                SessionId = AppState.SessionId,
                StartTime = DateTime.UtcNow,
                Provider = providerName,
                Model = modelName,
                PermissionMode = AppState.CurrentPermissionMode,
                WorkspacePath = AppState.CurrentCwd
            };
            await _sessionStore.InitializeAsync(record);
            AnsiConsole.MarkupLine($"[grey]Session initialized:[/] [link]{_sessionStore.SessionDir}[/]");
        }

        private async Task SyncTaskBoardAsync()
        {
            if (_sessionStore == null) return;

            var board = new AgentTaskBoardRecord
            {
                SessionId = AppState.SessionId,
                LastUpdatedAt = DateTime.UtcNow,
                Tasks = AppState.Tasks.Values.Select(t =>
                {
                    var record = new AgentTaskRecord
                    {
                        Id = t.Id,
                        Title = t is CoordinateTask ct ? ct.Title : t.Id,
                        Description = t is CoordinateTask ct2 ? ct2.Description : t.Type,
                        Status = t.Status,
                        AssignedAgent = t is CoordinateTask ct3 ? ct3.AssignedAgent : null
                    };

                    if (t is CoordinateTask ct4)
                    {
                        record.Progress = ct4.ReadinessScore;
                        record.ExtraData["Phase"] = ct4.CurrentPhase.ToString();
                        record.ExtraData["Blockers"] = ct4.Blockers;
                    }
                    return record;
                }).ToList()
            };

            await _sessionStore.SaveTaskBoardAsync(board);
        }

        /// <summary>
        /// 硫붿씤 硫붿떆吏 ?섏떊 猷⑦봽瑜??쒖옉?⑸땲??
        /// </summary>
        public async Task ListenAsync(CancellationToken ct = default)
        {
            AnsiConsole.MarkupLine("[bold cyan][[Agent]][/] Consumer loop started. Waiting for messages...");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1. ?ъ슜???낅젰 ?섏떊
                    var context = await _broker.ReadAsync(ct);
                    string finalPrompt = context.Text;

                    // 2. ?뱀닔 紐낅졊 泥섎━: !reflect (?먭? ?깆같 諛?媛?대뱶 ?낅뜲?댄듃)
                    if (finalPrompt.Trim().ToLower() == "!reflect")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Analyzing agent_trajectories...[/]");
                        string diagnosis = await GenerateReflectionSummaryAsync();
                        if (string.IsNullOrEmpty(diagnosis))
                        {
                            AnsiConsole.MarkupLine("[red]遺꾩꽍??沅ㅼ쟻(agent_trajectories) ?곗씠?곌? ?놁뒿?덈떎.[/]");
                            await context.Output.WriteAsync("No trajectories found to reflect on.");
                            Console.Write("\n> ");
                            continue;
                        }

                        // 吏꾨떒 寃곌낵瑜?諛뷀깢?쇰줈 Self-Healing 媛?대뱶 ?낅뜲?댄듃
                        SelfHealingService.Instance.UpdateGuide(diagnosis);
                        AnsiConsole.MarkupLine("[bold green]SELF_HEAL_GUIDE.md updated successfully.[/]");

                        finalPrompt = "?뱀떊??理쒓렐 沅ㅼ쟻 ?듦퀎 吏꾨떒?쒖엯?덈떎.\n\n" + diagnosis + "\n\n???곗씠??湲곕컲 ?ㅽ뙣 ?듦퀎瑜?諛뷀깢?쇰줈 ?묒뾽 諛⑹떇???깆같 諛??ъ꽕怨꾪븯怨??먯쑉?곸쑝濡?`Skills` ?대뜑 ?댁뿉 留덊겕?ㅼ슫 ?뚯씪(?? `Skills/SKILL.md`)???앹꽦/?낅뜲?댄듃?섏뿬 ?쇰뱶諛?猷⑦봽瑜??꾩꽦?섏꽭?? Skills ?대뜑媛 ?녿떎硫??곗꽑 ?앹꽦?섏떗?쒖삤. 媛?대뱶?쇱씤? 諛섎뱶??留덊겕?ㅼ슫 ?щ㎎?쇰줈 援ъ껜?곸쑝濡??묒꽦?섏꽭?? ?낅뜲?댄듃 ???듭떖 蹂寃쎌궗??쓣 ?쒓뎅?대줈 蹂닿퀬?섏꽭??";
                    }
                    else
                    {
                        // 3. ?명뀗??湲곕컲 荑쇰━ ?쇱슦??(?? ?먯뿰?대? ?쒖뒪??紐낅졊?대줈 蹂??
                        string? routedCommand = QueryRouter.Route(context.Text);

                        // 4. ?쒖뒪??紐낅졊??媛濡쒖콈湲?諛?泥섎━
                        var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output, context.Approval) : context;
                        if (await HandleSystemCommand(effectiveContext, ct))
                        {
                            Console.Write("\n> ");
                            continue;
                        }
                        finalPrompt = effectiveContext.Text;
                    }

                    // ?묒뾽 怨듦컙 ?ㅼ젙 ?뺤씤 (蹂댁븞 諛?寃쎈줈 湲곗???
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Conversations are blocked. Use [bold]/setworkspace <path>[/] first.");
                        await context.Output.WriteAsync("Error: Workspace is not set. Conversations are blocked. Use /setworkspace <path> first.");
                        Console.Write("\n> ");
                        continue;
                    }

                    // 5. Smart Routing: ?낅젰??蹂듭옟?꾩? 鍮꾩슜, ?깃났瑜좎쓣 怨좊젮?섏뿬 理쒖쟻??LLM ?좎젙
                    var decision = _router.Route(finalPrompt);
                    ILLMProvider provider = decision.SelectedProvider switch
                    {
                        "gemini" => _serviceProvider.GetRequiredService<GeminiProvider>(),
                        "gemini-cli" => _serviceProvider.GetRequiredService<GeminiCliProvider>(),
                        "ollama" => _serviceProvider.GetRequiredService<OllamaProvider>(),
                        _ => _serviceProvider.GetRequiredService<ClaudeService>()
                    };

                    AnsiConsole.MarkupLine($"[grey]Routing:[/] [bold cyan]{decision.SelectedProvider}[/] ([italic]{decision.SelectedModel}[/]) - [grey]{decision.Reason ?? "Auto"}[/]");

                    // 6. RAG(Retrieval-Augmented Generation): 怨쇨굅???좎궗???묒뾽 湲곗뼲 異붿텧
                    string relevantContext = await RetrieveRelevantMemoriesAsync(finalPrompt);
                    if (!string.IsNullOrEmpty(relevantContext))
                    {
                        AnsiConsole.MarkupLine("[bold blue]?쭬 Context Retrieved:[/] Found relevant past interactions in agent_memory.");
                    }
                    string promptWithContext = relevantContext + finalPrompt;

                    // 7. ?ш퀬-?됰룞-愿李?猷⑦봽 ?ㅽ뻾
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

        /// <summary>
        /// ?ъ슜?먯쓽 ?꾨＼?꾪듃? 愿?⑤맂 怨쇨굅 湲곕줉??寃?됲븯??而⑦뀓?ㅽ듃瑜?利앷컯?⑸땲??
        /// </summary>
        private async Task<string> RetrieveRelevantMemoriesAsync(string userPrompt)
        {
            if (_embedding == null) return "";

            var sw = Stopwatch.StartNew();
            float[]? targetVector = null;

            // ?④퀎 1: TeruTeruPandas L2 罹먯떆(embedding_cache)?먯꽌 湲곗〈 ?꾨쿋??寃??
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

            // ?④퀎 2: 罹먯떆???녿뒗 寃쎌슦 API瑜??몄텧?섏뿬 ?꾨쿋???앹꽦 諛????
            if (targetVector == null)
            {
                try { targetVector = await _embedding.GetEmbeddingAsync(userPrompt); } catch { }

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

            // ?④퀎 3: 踰≫꽣 ?좎궗??湲곕컲 硫붾え由?寃??(Vector Search)
            string result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory")) return "";
                var df = u.GetTableOrThrow("agent_memory");
                if (df.RowCount == 0) return "";

                DataFrame topMemories;

                // ?좏슚??踰≫꽣媛 ?덇퀬 Embedding 而щ읆??議댁옱?섎뒗 寃쎌슦 SIMD 媛??肄붿궗???좎궗??怨꾩궛
                if (targetVector != null && targetVector.Length > 0 && df.Columns.Contains("Embedding"))
                {
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

                    // ?좎궗?꾧? ?덈Т ??굅??寃곌낵媛 ?녿뒗 寃쎌슦 ?ㅼ썙??留ㅼ묶?쇰줈 Fallback
                    var topSim = topMemories.Columns.Contains("Similarity") ? (double)(topMemories["Similarity"].GetValue(0) ?? -1.0) : -1.0;
                    if (topSim <= 0)
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                }
                else
                {
                    // ?④퀎 4: 踰≫꽣 寃?됱씠 遺덇??ν븳 寃쎌슦 ?ㅼ썙??留ㅼ묶 ?섑뻾
                    topMemories = SearchByKeywords(df, userPrompt);
                }

                if (topMemories.RowCount == 0) return "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\n[?쒖뒪??二쇱쓽: 怨쇨굅 ?곹샇?묒슜 湲곕줉 以??꾩옱 ?붿껌怨?愿?⑤맂 ?댁슜??諛쒓껄?섏뿀?듬땲?? 李멸퀬?섏떗?쒖삤.]");
                for (int i = 0; i < topMemories.RowCount; i++)
                {
                    sb.AppendLine($"--- 湲곕줉 (?몃뜳?? {i}) ---");
                    sb.AppendLine($"?붿껌: {topMemories["UserPrompt"].GetValue(i)}");
                    sb.AppendLine($"??? {topMemories["AgentResponse"].GetValue(i)}");
                }
                sb.AppendLine("--------------------------------------------------------------------------\n");
                return sb.ToString();
            });

            sw.Stop();
            if (sw.ElapsedMilliseconds > 200)
            {
                AnsiConsole.MarkupLine($"[yellow]??Performance Warning:[/] RAG retrieval took {sw.ElapsedMilliseconds}ms.");
            }
            return result;
        }

        /// <summary>
        /// ?꾨＼?꾪듃?먯꽌 異붿텧???ㅼ썙?쒕? 湲곕컲?쇰줈 硫붾え由щ? 寃?됲빀?덈떎.
        /// </summary>
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

        /// <summary>
        /// ?섏쭛???먯씠?꾪듃 沅ㅼ쟻(Trajectories)??遺꾩꽍?섏뿬 ?꾧뎄 ?ъ슜 ?듦퀎 諛??ㅽ뙣 ?먯씤??遺꾩꽍?⑸땲??
        /// </summary>
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
                sb.AppendLine("=== 吏?ν삎 ?듦퀎 吏꾨떒??(DataUniverse Agent Trajectories) ===");
                sb.AppendLine($"珥????몄텧 ?잛닔: {totalCount}");
                foreach (var s in stats)
                {
                    sb.AppendLine($"- {s.ToolName} : {s.Total}???쒕룄, {s.Fails}???ㅽ뙣 (?ㅽ뙣??{s.Rate * 100:0.1}%)");
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

        /// <summary>
        /// ?쒖뒪??紐낅졊?대? 泥섎━?⑸땲?? (!) ?먮뒗 (/)濡??쒖옉?섎뒗 紐낅졊?대? 媛먯??⑸땲??
        /// </summary>
        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            string text = context.Text.Trim();
            if (!(text.StartsWith("!") || text.StartsWith("/"))) return false;

            string[] parts = text.Split(' ', 2);
            string baseCmd = parts[0].TrimStart('!', '/').ToLowerInvariant();

            switch (baseCmd)
            {
                case "status":
                    var memProcess = Process.GetCurrentProcess();
                    long memoryUsedMB = GC.GetTotalMemory(false) / 1024 / 1024;

                    var statusTable = new Table().Border(TableBorder.Rounded);
                    statusTable.AddColumn("[bold cyan]Property[/]");
                    statusTable.AddColumn("[bold yellow]Value[/]");

                    statusTable.AddRow("Session ID", AppState.SessionId);
                    statusTable.AddRow("Workspace", AppState.CurrentCwd ?? "[red]Not Set[/]");
                    statusTable.AddRow("Active Provider", AppState.ActiveProvider);
                    statusTable.AddRow("Active Model", AppState.ActiveModel);
                    statusTable.AddRow("Memory Usage", $"{memoryUsedMB} MB");
                    statusTable.AddRow("Permission Mode", AppState.CurrentPermissionMode.ToString());

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

                    await context.Output.WriteAsync($"Status: {AppState.ActiveProvider}/{AppState.ActiveModel}, {AppState.Tasks.Count} active tasks.");
                    return true;

                case "resume":
                    if (parts.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[bold yellow]Usage:[/] !resume <sessionId>");
                        await context.Output.WriteAsync("Usage: !resume <sessionId>");
                        return true;
                    }

                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Use /setworkspace first.");
                        await context.Output.WriteAsync("Error: Workspace is not set.");
                        return true;
                    }

                    string targetId = parts[1].Trim();
                    var sessionRecord = await AgentSessionStore.LoadSessionRecordAsync(AppState.CurrentCwd, targetId);
                    if (sessionRecord == null)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Error:[/] Session '{targetId}' not found in workspace.");
                        await context.Output.WriteAsync($"Error: Session '{targetId}' not found.");
                        return true;
                    }

                    AnsiConsole.MarkupLine($"[bold green]Session '{targetId}' found.[/]");
                    AnsiConsole.MarkupLine($"- Start Time: {sessionRecord.StartTime}");
                    AnsiConsole.MarkupLine($"- Provider: {sessionRecord.Provider}");
                    AnsiConsole.MarkupLine($"- Model: {sessionRecord.Model}");
                    AnsiConsole.MarkupLine($"- Permission: {sessionRecord.PermissionMode}");

                    await context.Output.WriteAsync($"Session {targetId} metadata loaded. (Full state resumption partial)");
                    return true;

                case "build":
                    AnsiConsole.MarkupLine("[bold blue]Building project...[/]");
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
                    return true;

                case "login":
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
                        AppState.IsProviderExplicitlySet = true;
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
                        AppState.IsProviderExplicitlySet = true;
                        AnsiConsole.MarkupLine($"[bold cyan]Switching provider to {providerName}...[/]");
                        await context.Output.WriteAsync($"Provider switched to {providerName} and API key has been saved.");
                    }
                    return true;

                case "tools":
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

                case "reload":
                    string pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins");
                    _orchestrator.ReloadDynamicPlugins(pluginsDir);

                    AnsiConsole.MarkupLine($"[bold green]Dynamic plugins have been fully hot-reloaded from RAM! ({pluginsDir})[/]");
                    await context.Output.WriteAsync("System plugins metadata and runtime assemblies refreshed.");
                    return true;

                case "prune":
                    int days = 7;
                    if (parts.Length > 1 && int.TryParse(parts[1], out int d)) days = d;
                    AnsiConsole.MarkupLine($"[bold cyan]Pruning agent_trajectories older than {days} days...[/]");
                    await SelfHealingService.Instance.PruneTrajectoriesAsync(days);
                    await context.Output.WriteAsync($"Trajectory pruning completed (Retention: {days} days).");
                    return true;

                case "save":
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

        private async Task LogProgressAsync(string type, string? message = null, object? data = null)
        {
            if (_sessionStore == null) return;
            var evt = new AgentProgressEvent
            {
                AgentId = AppState.ActiveProvider,
                Type = type,
                Message = message,
                Data = data
            };
            await _sessionStore.AppendProgressAsync(AppState.ActiveProvider, evt);
        }

        /// <summary>
        /// ?ㅼ쨷 ??異붾줎 猷⑦봽瑜??ㅽ뻾?⑸땲?? LLM???묐떟怨??꾧뎄 ?몄텧??諛섎났?곸쑝濡?泥섎━?⑸땲??
        /// </summary>
        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, string model, IUserApprovalHandler? approval = null, CancellationToken ct = default)
        {
            await EnsureSessionInitializedAsync(provider.Name, model);
            await LogProgressAsync("UserPrompt", message: userPrompt);

            string currentPrompt = userPrompt;
            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 200; // 臾댄븳 猷⑦봽 諛⑹?瑜??꾪븳 Circuit Breaker

            var sw = Stopwatch.StartNew();
            bool hasError = false;
            string lastTurnResponse = "";

            // --- ?ш퀬-?됰룞-愿李?Reasoning) 猷⑦봽 ?쒖옉 ---
            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                turnCount++;
                var toolCalls = new List<ToolUseRequest>();
                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");
                    await LogProgressAsync("ThinkingStart", message: $"Turn {turnCount}");

                    // ?④퀎 1: LLM?먭쾶 ?꾩옱 ?곹솴???꾨떖?섍퀬 ?ㅽ듃由щ컢 ?묐떟 ?섏떊
                    string turnPrompt = isFirstTurn ? currentPrompt : "Proceed based on previous tool results.";

                    // Gemini fix: Do not add regular user prompt immediately after function response
                    if (!isFirstTurn && (provider.Name == "gemini" || provider.Name == "gemini-cli"))
                    {
                        turnPrompt = "";
                    }

                    await foreach (var evt in provider.StreamQueryAsync(turnPrompt, model: model, ct: ct))
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
                        await LogProgressAsync("TextDelta", message: lastTurnResponse);
                    }
                }
                catch (Exception ex)
                {
                    hasError = true;
                    string errorMsg = $"Error ({provider.Name}): {ex.Message}";
                    AnsiConsole.Console.Write(new Markup($"\n[bold red]{Markup.Escape(errorMsg)}[/]\n"));
                    await output.WriteAsync(errorMsg);
                    await LogProgressAsync("Error", message: errorMsg);
                    break;
                }

                isFirstTurn = false;

                // ?④퀎 2: ?꾧뎄 ?몄텧(Tool Call)??諛쒖깮??寃쎌슦 ?ㅽ뻾 泥섎━
                if (toolCalls.Count > 0)
                {
                    foreach (var tc in toolCalls)
                    {
                        AnsiConsole.MarkupLine($"[grey]?썱截? [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                        await LogProgressAsync("ToolCall", message: tc.Name, data: tc.Input);
                    }

                    // ?꾧뎄 ?ㅼ??ㅽ듃?덉씠?곕? ?듯븳 諛곗튂 ?ㅽ뻾 (蹂댁븞 寃??諛?蹂묐젹 泥섎━ ?ы븿)
                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { }, approval, ct);

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";
                        await LogProgressAsync("ToolResult", message: result.ToolUseId, data: new { result.IsError, result.Content });

                        // ?대?吏 ?앹꽦 寃곌낵 泥섎━ (Discord ???몃? 異쒕젰 ?곕룞)
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
                            AnsiConsole.MarkupLine($"  [red]??{escapedId}:[/] [grey]{escapedSummary}[/]");
                        else
                            AnsiConsole.MarkupLine($"  [green]??{escapedId}:[/] [grey]{escapedSummary}[/]");

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content?.ToString() ?? "Success", is_error = result.IsError });
                    }

                    // ?④퀎 3: [?곗씠??吏꾪솕 ?꾨왂] ?ㅽ뻾 沅ㅼ쟻 ?섏쭛 諛??붾젅硫뷀듃由?湲곕줉
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
                        // 鍮꾨룞湲?諛깃렇?쇱슫?쒕줈 沅ㅼ쟻 ?곗씠?????(?깅뒫 ???諛⑹?)
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

                    // ?④퀎 4: 而⑦뀓?ㅽ듃 ?뺤텞 諛??꾧뎄 寃곌낵 ?쇰뱶諛?
                    // 寃곌낵媛 ?덈Т 湲?寃쎌슦 ?붿빟?섏뿬 LLM?먭쾶 ?ㅼ떆 ?꾨떖 (?좏겙 ?덉빟 諛?而⑦뀓?ㅽ듃 ?좎?)
                    // Gemini requires a functionResponse for every functionCall. Collapsing tool
                    // results into plain text breaks that protocol when a turn has many tool calls.
                    var processedResults = provider.Name == "gemini"
                        ? toolResults
                        : ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
                    continue;
                }

                // ???댁긽???꾧뎄 ?몄텧???놁쑝硫?猷⑦봽 醫낅즺
                break;
            }

            await output.CompleteAsync(lastTurnResponse);

            sw.Stop();
            // ?쇱슦???깅뒫 硫뷀듃由??낅뜲?댄듃 (吏???대룞 ?됯퇏 諛섏쁺)
            _router.UpdateMetric(provider.Name, sw.Elapsed.TotalMilliseconds, hasError);

            // ?④퀎 5: [RAG Ingestion] ?깃났?곸씤 ?곹샇?묒슜 湲곕줉 ???
            if (!hasError && !string.IsNullOrEmpty(lastTurnResponse))
            {
                if (_sessionStore != null)
                {
                    await _sessionStore.SaveResultAsync(provider.Name, lastTurnResponse);
                    await SyncTaskBoardAsync();
                }

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
                AnsiConsole.MarkupLine("\n[bold red]?썞 Circuit Breaker Hit![/]");
            }
        }

        private static readonly Regex KeywordRegex = new(@"\b\w{4,}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "this", "that", "there", "their", "where", "which", "could", "should", "would", "about", "above", "after", "again"
        };

        /// <summary>
        /// ?띿뒪?몄뿉??寃?됱뿉 ?ъ슜??二쇱슂 ?ㅼ썙?쒕? 異붿텧?⑸땲??
        /// </summary>
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
