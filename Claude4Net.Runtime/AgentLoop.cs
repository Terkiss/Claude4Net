using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Spectre.Console;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using System.Text.Json;
using Claude4Net.Api;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using TeruTeruPandas.Core;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// Claude4Net???µÏã¨ ?§Ìñâ ?îÏßÑ?ºÎ°ú, ?êÏù¥?ÑÌä∏???¨Í≥†-?âÎèô-Í¥ÄÏ∞?Reasoning Loop) Í≥ºÏ†ï??Ï¥ùÍ¥Ñ?©Îãà??
    /// ?ÖÎ†• Ï≤òÎ¶¨, ?§Îßà???ºÏö∞?? RAG Í≤Ä?? ?ÑÍµ¨ ?§Ìñâ Î∞??êÍ? ÏπòÏú†Î•??ÑÌïú Í∂§Ï†Å ?òÏßë???¥Îãπ?©Îãà??
    /// </summary>
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly IEmbeddingProvider? _embedding;
        private IAgentEventStore CurrentEventStore => new FileAgentEventStore(AppState.CurrentCwd ?? Directory.GetCurrentDirectory());
        private readonly IAgentEventBroadcaster? _broadcaster;
        private AgentSessionStore? _sessionStore;
        private long _currentVersion = 0;

        /// <summary>
        /// AgentLoop?????∏Ïä§?¥Ïä§Î•?Ï¥àÍ∏∞?îÌï©?àÎã§.
        /// </summary>
        public AgentLoop(ToolOrchestrator orchestrator, IServiceProvider serviceProvider, IInputBroker broker, ISmartRouter router, IEmbeddingProvider? embedding = null, IAgentEventBroadcaster? broadcaster = null)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
            _router = router;
            _embedding = embedding;
            _broadcaster = broadcaster;

        }

        private async Task EnsureSessionInitializedAsync(string providerName, string modelName)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return;

            // Ensure session store matches current workspace
            if (_sessionStore != null && _sessionStore.WorkspaceRoot == AppState.CurrentCwd && _sessionStore.SessionId == AppState.SessionId)
                return;

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

            await AppendEventAsync(new SessionStartedEvent
            {
                WorkspacePath = AppState.CurrentCwd,
                Provider = providerName,
                Model = modelName
            });
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
        /// Î©îÏù∏ Î©îÏãúÏßÄ ?òÏã† Î£®ÌîÑÎ•??úÏûë?©Îãà??
        /// </summary>
        public async Task ListenAsync(CancellationToken ct = default)
        {
            AnsiConsole.MarkupLine("[bold cyan][[Agent]][/] Consumer loop started. Waiting for messages...");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1. ?¨Ïö©???ÖÎ†• ?òÏã†
                    var context = await _broker.ReadAsync(ct);
                    string finalPrompt = context.Text;

                    // 2. ?πÏàò Î™ÖÎ†π Ï≤òÎ¶¨: !reflect (?êÍ? ?±Ï∞∞ Î∞?Í∞Ä?¥Îìú ?ÖÎç∞?¥Ìä∏)
                    if (finalPrompt.Trim().ToLower() == "!reflect")
                    {
                        AnsiConsole.MarkupLine("[bold cyan]Analyzing agent_trajectories...[/]");
                        string diagnosis = await GenerateReflectionSummaryAsync();
                        if (string.IsNullOrEmpty(diagnosis))
                        {
                            AnsiConsole.MarkupLine("[red]Î∂ÑÏÑù??Í∂§Ï†Å(agent_trajectories) ?∞Ïù¥?∞Í? ?ÜÏäµ?àÎã§.[/]");
                            await context.Output.WriteAsync("No trajectories found to reflect on.");
                            Console.Write("\n> ");
                            continue;
                        }

                        // ÏßÑÎã® Í≤∞Í≥ºÎ•?Î∞îÌÉï?ºÎ°ú Self-Healing Í∞Ä?¥Îìú ?ÖÎç∞?¥Ìä∏
                        SelfHealingService.Instance.UpdateGuide(diagnosis);
                        AnsiConsole.MarkupLine("[bold green]SELF_HEAL_GUIDE.md updated successfully.[/]");

                        finalPrompt = "?πÏã†??ÏµúÍ∑º Í∂§Ï†Å ?µÍ≥Ñ ÏßÑÎã®?úÏûÖ?àÎã§.\n\n" + diagnosis + "\n\n???∞Ïù¥??Í∏∞Î∞ò ?§Ìå® ?µÍ≥ÑÎ•?Î∞îÌÉï?ºÎ°ú ?ëÏóÖ Î∞©Ïãù???±Ï∞∞ Î∞??¨ÏÑ§Í≥ÑÌïòÍ≥??êÏú®?ÅÏúºÎ°?`Skills` ?¥Îçî ?¥Ïóê ÎßàÌÅ¨?§Ïö¥ ?åÏùº(?? `Skills/SKILL.md`)???ùÏÑ±/?ÖÎç∞?¥Ìä∏?òÏó¨ ?ºÎìúÎ∞?Î£®ÌîÑÎ•??ÑÏÑ±?òÏÑ∏?? Skills ?¥ÎçîÍ∞Ä ?ÜÎã§Î©??∞ÏÑ† ?ùÏÑ±?òÏã≠?úÏò§. Í∞Ä?¥Îìú?ºÏù∏?Ä Î∞òÎìú??ÎßàÌÅ¨?§Ïö¥ ?¨Îß∑?ºÎ°ú Íµ¨Ï≤¥?ÅÏúºÎ°??ëÏÑ±?òÏÑ∏?? ?ÖÎç∞?¥Ìä∏ ???µÏã¨ Î≥ÄÍ≤ΩÏÇ¨??ùÑ ?úÍµ≠?¥Î°ú Î≥¥Í≥†?òÏÑ∏??";
                    }
                    else
                    {
                        // 3. ?∏ÌÖê??Í∏∞Î∞ò ÏøºÎ¶¨ ?ºÏö∞??(?? ?êÏó∞?¥Î? ?úÏä§??Î™ÖÎ†π?¥Î°ú Î≥Ä??
                        string? routedCommand = QueryRouter.Route(context.Text);

                        // 4. ?úÏä§??Î™ÖÎ†π??Í∞ÄÎ°úÏ±ÑÍ∏?Î∞?Ï≤òÎ¶¨
                        var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output, context.Approval) : context;
                        if (await HandleSystemCommand(effectiveContext, ct))
                        {
                            Console.Write("\n> ");
                            continue;
                        }
                        finalPrompt = effectiveContext.Text;
                    }

                    // ?ëÏóÖ Í≥µÍ∞Ñ ?§Ï†ï ?ïÏù∏ (Î≥¥Ïïà Î∞?Í≤ΩÎ°ú Í∏∞Ï???
                    if (string.IsNullOrEmpty(AppState.CurrentCwd))
                    {
                        AnsiConsole.MarkupLine("[bold red]Error:[/] Workspace is not set. Conversations are blocked. Use [bold]/setworkspace <path>[/] first.");
                        await context.Output.WriteAsync("Error: Workspace is not set. Conversations are blocked. Use /setworkspace <path> first.");
                        Console.Write("\n> ");
                        continue;
                    }

                    // 5. Smart Routing: ?ÖÎ†•??Î≥µÏû°?ÑÏ? ÎπÑÏö©, ?±Í≥µÎ•†ÏùÑ Í≥†Î†§?òÏó¨ ÏµúÏ†Å??LLM ?†Ï†ï
                    var decision = _router.Route(finalPrompt);
                    ILLMProvider provider = decision.SelectedProvider switch
                    {
                        "gemini" => _serviceProvider.GetRequiredService<GeminiProvider>(),
                        "gemini-cli" => _serviceProvider.GetRequiredService<GeminiCliProvider>(),
                        "ollama" => _serviceProvider.GetRequiredService<OllamaProvider>(),
                        _ => _serviceProvider.GetRequiredService<ClaudeService>()
                    };

                    AnsiConsole.MarkupLine($"[grey]Routing:[/] [bold cyan]{decision.SelectedProvider}[/] ([italic]{decision.SelectedModel}[/]) - [grey]{decision.Reason ?? "Auto"}[/]");

                    // 6. RAG(Retrieval-Augmented Generation): Í≥ºÍ±∞???†ÏÇ¨???ëÏóÖ Í∏∞Ïñµ Ï∂îÏ∂ú
                    string relevantContext = await RetrieveRelevantMemoriesAsync(finalPrompt);
                    if (!string.IsNullOrEmpty(relevantContext))
                    {
                        AnsiConsole.MarkupLine("[bold blue]?ß† Context Retrieved:[/] Found relevant past interactions in agent_memory.");
                    }
                    string promptWithContext = relevantContext + finalPrompt;

                    // 7. ?¨Í≥†-?âÎèô-Í¥ÄÏ∞?Î£®ÌîÑ ?§Ìñâ
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
        /// ?¨Ïö©?êÏùò ?ÑÎ°¨?ÑÌä∏?Ä Í¥Ä?®Îêú Í≥ºÍ±∞ Í∏∞Î°ù??Í≤Ä?âÌïò??Ïª®ÌÖç?§Ìä∏Î•?Ï¶ùÍ∞ï?©Îãà??
        /// </summary>
        private async Task<string> RetrieveRelevantMemoriesAsync(string userPrompt)
        {
            if (_embedding == null) return "";

            var sw = Stopwatch.StartNew();
            float[]? targetVector = null;

            // ?®Í≥Ñ 1: TeruTeruPandas L2 Ï∫êÏãú(embedding_cache)?êÏÑú Í∏∞Ï°¥ ?ÑÎ≤†??Í≤Ä??
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

            // ?®Í≥Ñ 2: Ï∫êÏãú???ÜÎäî Í≤ΩÏö∞ APIÎ•??∏Ï∂ú?òÏó¨ ?ÑÎ≤†???ùÏÑ± Î∞??Ä??
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

            // ?®Í≥Ñ 3: Î≤°ÌÑ∞ ?†ÏÇ¨??Í∏∞Î∞ò Î©îÎ™®Î¶?Í≤Ä??(Vector Search)
            string result = await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                if (!u.ContainsTable("agent_memory")) return "";
                var df = u.GetTableOrThrow("agent_memory");
                if (df.RowCount == 0) return "";

                DataFrame topMemories;

                // ?†Ìö®??Î≤°ÌÑ∞Í∞Ä ?àÍ≥† Embedding Ïª¨Îüº??Ï°¥Ïû¨?òÎäî Í≤ΩÏö∞ SIMD Í∞Ä??ÏΩîÏÇ¨???†ÏÇ¨??Í≥ÑÏÇ∞
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

                    // ?†ÏÇ¨?ÑÍ? ?àÎ¨¥ ??±∞??Í≤∞Í≥ºÍ∞Ä ?ÜÎäî Í≤ΩÏö∞ ?§Ïõå??Îß§Ïπ≠?ºÎ°ú Fallback
                    var topSim = topMemories.Columns.Contains("Similarity") ? (double)(topMemories["Similarity"].GetValue(0) ?? -1.0) : -1.0;
                    if (topSim <= 0)
                    {
                        topMemories = SearchByKeywords(df, userPrompt);
                    }
                }
                else
                {
                    // ?®Í≥Ñ 4: Î≤°ÌÑ∞ Í≤Ä?âÏù¥ Î∂àÍ??•Ìïú Í≤ΩÏö∞ ?§Ïõå??Îß§Ïπ≠ ?òÌñâ
                    topMemories = SearchByKeywords(df, userPrompt);
                }

                if (topMemories.RowCount == 0) return "";

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("\n[?úÏä§??Ï£ºÏùò: Í≥ºÍ±∞ ?ÅÌò∏?ëÏö© Í∏∞Î°ù Ï§??ÑÏû¨ ?îÏ≤≠Í≥?Í¥Ä?®Îêú ?¥Ïö©??Î∞úÍ≤¨?òÏóà?µÎãà?? Ï∞∏Í≥†?òÏã≠?úÏò§.]");
                for (int i = 0; i < topMemories.RowCount; i++)
                {
                    sb.AppendLine($"--- Í∏∞Î°ù (?∏Îç±?? {i}) ---");
                    sb.AppendLine($"?îÏ≤≠: {topMemories["UserPrompt"].GetValue(i)}");
                    sb.AppendLine($"?Ä?? {topMemories["AgentResponse"].GetValue(i)}");
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
        /// ?ÑÎ°¨?ÑÌä∏?êÏÑú Ï∂îÏ∂ú???§Ïõå?úÎ? Í∏∞Î∞ò?ºÎ°ú Î©îÎ™®Î¶¨Î? Í≤Ä?âÌï©?àÎã§.
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
        /// ?òÏßë???êÏù¥?ÑÌä∏ Í∂§Ï†Å(Trajectories)??Î∂ÑÏÑù?òÏó¨ ?ÑÍµ¨ ?¨Ïö© ?µÍ≥Ñ Î∞??§Ìå® ?êÏù∏??Î∂ÑÏÑù?©Îãà??
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
                sb.AppendLine("=== ÏßÄ?•Ìòï ?µÍ≥Ñ ÏßÑÎã®??(DataUniverse Agent Trajectories) ===");
                sb.AppendLine($"Ï¥????∏Ï∂ú ?üÏàò: {totalCount}");
                foreach (var s in stats)
                {
                    sb.AppendLine($"- {s.ToolName} : {s.Total}???úÎèÑ, {s.Fails}???§Ìå® (?§Ìå®??{s.Rate * 100:0.1}%)");
                }

                var failCategories = categories.Where(c => c != "Success" && c != "Unknown")
                                               .GroupBy(c => c)
                                               .OrderByDescending(g => g.Count());

                if (failCategories.Any())
                {
                    sb.AppendLine("\nΩ«∆– ƒ´≈◊∞Ì∏Æ ∫–∆˜:");
                    foreach (var c in failCategories) sb.AppendLine($" - {c.Key}: {c.Count()}»∏");
                }

                var topErrors = errorReasons.Where(e => !string.IsNullOrWhiteSpace(e) && e.Length > 3)
                                            .GroupBy(e => e)
                                            .OrderByDescending(g => g.Count())
                                            .Take(3);

                if (topErrors.Any())
                {
                    sb.AppendLine("\n¡÷ø‰ πﬂª˝ ø°∑Ø ≥ªøÎ (Top 3):");
                    foreach (var e in topErrors) sb.AppendLine($" - [{e.Count()}»∏ πﬂª˝] {e.Key.Replace("\n", " ").Substring(0, Math.Min(150, e.Key.Length))}");
                }
                return sb.ToString();
            });
        }

        /// <summary>
        /// ?úÏä§??Î™ÖÎ†π?¥Î? Ï≤òÎ¶¨?©Îãà?? (!) ?êÎäî (/)Î°??úÏûë?òÎäî Î™ÖÎ†π?¥Î? Í∞êÏ??©Îãà??
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

                    var resumeEvents = await CurrentEventStore.GetEventsAsync(targetId);
                    var resumeSnapshot = await CurrentEventStore.GetLatestSnapshotAsync(targetId);
                    var state = AgentStateReconstructor.Reconstruct(resumeEvents, resumeSnapshot);

                    AppState.SessionId = targetId;
                    AppState.ActiveProvider = sessionRecord.Provider;
                    AppState.ActiveModel = sessionRecord.Model;
                    AppState.CurrentPermissionMode = sessionRecord.PermissionMode;

                    ILLMProvider resumeProvider = sessionRecord.Provider switch
                    {
                        "gemini" => _serviceProvider.GetRequiredService<GeminiProvider>(),
                        "gemini-cli" => _serviceProvider.GetRequiredService<GeminiCliProvider>(),
                        "ollama" => _serviceProvider.GetRequiredService<OllamaProvider>(),
                        _ => _serviceProvider.GetRequiredService<ClaudeService>()
                    };

                    resumeProvider.SetHistory(state.History);
                    _currentVersion = state.LastVersion;

                    AnsiConsole.MarkupLine($"[bold green]Session '{targetId}' state reconstructed:[/] {state.History.Count} messages, Version {state.LastVersion}");
                    await context.Output.WriteAsync($"Session {targetId} resumed. (History replayed to {resumeProvider.Name})");
                    return true;

                case "replay":
                    string replaySessionId = parts.Length > 1 ? parts[1].Trim() : AppState.SessionId;
                    var events = await CurrentEventStore.GetEventsAsync(replaySessionId);
                    if (!events.Any())
                    {
                        AnsiConsole.MarkupLine($"[yellow]No events found for session {replaySessionId}[/]");
                        await context.Output.WriteAsync($"No events found for session {replaySessionId}");
                        return true;
                    }

                    var replayTable = new Table().Border(TableBorder.Rounded);
                    replayTable.AddColumn("[bold cyan]Ver[/]");
                    replayTable.AddColumn("[bold yellow]Time[/]");
                    replayTable.AddColumn("[bold green]Event Type[/]");
                    replayTable.AddColumn("[bold white]Summary[/]");

                    foreach (var e in events)
                    {
                        string summary = e switch
                        {
                            UserPromptReceivedEvent up => up.Prompt,
                            AgentThoughtEvent at => at.Thought,
                            ToolCalledEvent tc => tc.ToolName,
                            ToolResultEvent tr => tr.Result,
                            FinalResponseGeneratedEvent fr => fr.Response,
                            _ => ""
                        };
                        if (summary.Length > 50) summary = summary.Substring(0, 47) + "...";
                        replayTable.AddRow(e.Version.ToString(), e.Timestamp.ToString("HH:mm:ss"), e.EventType, Markup.Escape(summary));
                    }
                    AnsiConsole.Write(new Panel(replayTable) { Header = new PanelHeader($"Event Replay: {replaySessionId}"), Border = BoxBorder.Rounded });
                    await context.Output.WriteAsync($"Replayed {events.Count()} events for session {replaySessionId}");
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

        private async Task AppendEventAsync(IAgentEvent @event)
        {
            if (@event is AgentEventBase baseEv)
            {
                baseEv.Version = ++_currentVersion;
            }
            await CurrentEventStore.AppendEventAsync(AppState.SessionId, @event);
            if (_broadcaster != null)
            {
                await _broadcaster.BroadcastAsync(@event);
            }
        }

        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, string model, IUserApprovalHandler? approval = null, CancellationToken ct = default)
        {
            SelfHealingService.Instance.ResetReflectionDepth();
            await EnsureSessionInitializedAsync(provider.Name, model);
            await LogProgressAsync("UserPrompt", message: userPrompt);
            await AppendEventAsync(new UserPromptReceivedEvent { Prompt = userPrompt });

            string currentPrompt = userPrompt;

            // Inject Self-Healing Guide if available (K026)
            string initialGuide = SelfHealingService.Instance.GetGuide();
            if (!string.IsNullOrEmpty(initialGuide) && !initialGuide.Contains("No active self-healing guidelines"))
            {
                currentPrompt = initialGuide + "\n\n" + userPrompt;
            }

            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 200; // Î¨¥Ìïú Î£®ÌîÑ Î∞©Ï?Î•??ÑÌïú Circuit Breaker

            var sw = Stopwatch.StartNew();
            bool hasError = false;
            string lastTurnResponse = "";

            // --- ªÁ∞Ì-«‡µø-∞¸¬˚(Reasoning) ∑Á«¡ Ω√¿€ ---
            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                // --- Self-Healing Pattern Detection & Strategy Switch (K026) ---
                var recentEvents = await CurrentEventStore.GetEventsAsync(AppState.SessionId);
                var pattern = SelfHealingService.Instance.ClassifyPattern(recentEvents.TakeLast(10));
                if (pattern != FailurePattern.None)
                {
                    if (SelfHealingService.Instance.IncrementReflectionDepth())
                    {
                        var directive = SelfHealingService.Instance.GenerateDirective(pattern);
                        AnsiConsole.MarkupLine($"[bold yellow]?? Self-Healing Triggered:[/] Detected {pattern}. Injecting directive.");
                        await LogProgressAsync("SelfHealing", message: $"Detected {pattern}", data: directive.Instruction);

                        // Inject directive immediately into current turn if not first turn
                        if (!isFirstTurn)
                        {
                            provider.AddMessage(new { role = "user", content = $"[SELF-HEALING] {directive.Instruction}" });
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]?? Max Reflection Depth Reached. Switching Strategy...[/]");
                        await LogProgressAsync("StrategySwitch", message: "Max reflection depth reached. Switching strategy.");

                        // Strategy Switch: Force model change if possible, or just break with error
                        // For K026, we'll simulate strategy switch by adding a strong intervention message
                        provider.AddMessage(new { role = "user", content = "CRITICAL: Previous strategy failed repeatedly. Abandon current approach and try a fundamentally different one." });
                    }
                }

                // --- ƒ¡≈ÿΩ∫∆Æ æ–√‡ ∑Œ¡˜ (K023) ---
                var history = provider.GetHistory();
                int currentTokens = provider.TokenCounter.CountTokens(history);
                int limit = provider.ContextLimit;

                if (currentTokens > limit * 0.8)
                {
                    AnsiConsole.MarkupLine($"[yellow]?? Context limit reached ({currentTokens}/{limit}). Compressing...[/]");
                    var compressedHistory = ContextCompressor.Compress(history.ToList(), provider.TokenCounter, limit);
                    provider.SetHistory(compressedHistory);
                    int compressedTokens = provider.TokenCounter.CountTokens(compressedHistory);
                    AnsiConsole.MarkupLine($"[green]? Context compressed: {currentTokens} -> {compressedTokens} tokens.[/]");
                    await LogProgressAsync("ContextCompressed", message: $"Compressed from {currentTokens} to {compressedTokens} tokens.");
                }

                turnCount++;
                var toolCalls = new List<ToolUseRequest>();

                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");
                    await LogProgressAsync("ThinkingStart", message: $"Turn {turnCount}");

                    // ?®Í≥Ñ 1: LLM?êÍ≤å ?ÑÏû¨ ?ÅÌô©???ÑÎã¨?òÍ≥† ?§Ìä∏Î¶¨Î∞ç ?ëÎãµ ?òÏã†
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
                        await AppendEventAsync(new AgentThoughtEvent { Thought = lastTurnResponse });
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

                // ?®Í≥Ñ 2: ?ÑÍµ¨ ?∏Ï∂ú(Tool Call)??Î∞úÏÉù??Í≤ΩÏö∞ ?§Ìñâ Ï≤òÎ¶¨
                if (toolCalls.Count > 0)
                {
                    foreach (var tc in toolCalls)
                    {
                        AnsiConsole.MarkupLine($"[grey]? [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                        await LogProgressAsync("ToolCall", message: tc.Name, data: tc.Input);
                        await AppendEventAsync(new ToolCalledEvent
                        {
                            ToolUseId = tc.Id,
                            ToolName = tc.Name,
                            Arguments = tc.Input?.ToString() ?? ""
                        });
                    }

                    // ?ÑÍµ¨ ?§Ï??§Ìä∏?àÏù¥?∞Î? ?µÌïú Î∞∞Ïπò ?§Ìñâ (Î≥¥Ïïà Í≤Ä??Î∞?Î≥ëÎ†¨ Ï≤òÎ¶¨ ?¨Ìï®)
                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { }, approval, ct);

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";
                        await LogProgressAsync("ToolResult", message: result.ToolUseId, data: new { result.IsError, result.Content });
                        await AppendEventAsync(new ToolResultEvent
                        {
                            ToolUseId = result.ToolUseId,
                            Result = summary,
                            IsError = result.IsError
                        });

                        // ?¥Î?ÏßÄ ?ùÏÑ± Í≤∞Í≥º Ï≤òÎ¶¨ (Discord ???∏Î? Ï∂úÎ†• ?∞Îèô)
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
                            AnsiConsole.MarkupLine($"  [red]? {escapedId}:[/] [grey]{escapedSummary}[/]");
                        else
                            AnsiConsole.MarkupLine($"  [green]? {escapedId}:[/] [grey]{escapedSummary}[/]");

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content?.ToString() ?? "Success", is_error = result.IsError });
                    }

                    // Step 3: [Data Evolution Strategy] Collect execution trajectories and telemetry
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
                        // Save trajectories in background to avoid performance overhead
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

                    // Step 4: Context summary and tool result feedback
                    // Í≤∞Í≥ºÍ∞Ä ?àÎ¨¥ Í∏?Í≤ΩÏö∞ ?îÏïΩ?òÏó¨ LLM?êÍ≤å ?§Ïãú ???ÑÎã¨ (?†ÌÅ∞ ?àÏïΩ Î∞?Ïª®ÌÖç?§Ìä∏ ?†Ï?)
                    // Gemini requires a functionResponse for every functionCall. Collapsing tool
                    // results into plain text breaks that protocol when a turn has many tool calls.
                    var processedResults = provider.Name == "gemini"
                        ? toolResults
                        : ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
                    continue;
                }

                // ???¥ÏÉÅ???ÑÍµ¨ ?∏Ï∂ú???ÜÏúºÎ©?Î£®ÌîÑ Ï¢ÖÎ£å
                break;
            }

            if (!string.IsNullOrEmpty(lastTurnResponse))
            {
                await AppendEventAsync(new FinalResponseGeneratedEvent { Response = lastTurnResponse });
            }

            await output.CompleteAsync(lastTurnResponse);

            sw.Stop();
            // ?ºÏö∞???±Îä• Î©îÌä∏Î¶??ÖÎç∞?¥Ìä∏ (ÏßÄ???¥Îèô ?âÍ∑† Î∞òÏòÅ)
            _router.UpdateMetric(provider.Name, sw.Elapsed.TotalMilliseconds, hasError);

            // ?®Í≥Ñ 5: [RAG Ingestion] ?±Í≥µ?ÅÏù∏ ?ÅÌò∏?ëÏö© Í∏∞Î°ù ?Ä??
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
                AnsiConsole.MarkupLine("\n[bold red]?? Circuit Breaker Hit![/]");
            }
        }

        private static readonly Regex KeywordRegex = new(@"\b\w{4,}\b", RegexOptions.Compiled);
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "this", "that", "there", "their", "where", "which", "could", "should", "would", "about", "above", "after", "again"
        };

        /// <summary>
        /// ?çÏä§?∏Ïóê??Í≤Ä?âÏóê ?¨Ïö©??Ï£ºÏöî ?§Ïõå?úÎ? Ï∂îÏ∂ú?©Îãà??
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
