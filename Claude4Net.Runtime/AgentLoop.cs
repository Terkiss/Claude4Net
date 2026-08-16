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
using Claude4Net.Runtime.Services;
using Claude4Net.Runtime.Handlers;
using FailurePattern = Claude4Net.Runtime.Services.FailurePattern;

namespace Claude4Net.Runtime
{
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly IEmbeddingProvider? _embedding;
        private IAgentEventStore CurrentEventStore => new FileAgentEventStore(AppState.CurrentCwd ?? Directory.GetCurrentDirectory());
        private readonly IAgentEventBroadcaster? _broadcaster;
        private readonly IAgentRunObserver _observer;
        
        private readonly RAGService _rag;
        private readonly TelemetryService _telemetry;
        private readonly ISelfHealingService _selfHealing;
        private readonly IAppState _appState;
        
        private AgentSessionStore? _sessionStore;
        private long _currentVersion = 0;
        private readonly OscillationDetector _oscillationDetector = new();
        private ILLMProvider? _resumedProvider;

        private string _lastResponseText = string.Empty;
        private int _lastTurnToolCallCount = 0;

        public AgentLoop(
            ToolOrchestrator orchestrator, 
            IServiceProvider serviceProvider, 
            IInputBroker broker, 
            ISmartRouter router, 
            RAGService rag,
            TelemetryService telemetry,
            ISelfHealingService selfHealing,
            IAppState appState,
            IEmbeddingProvider? embedding = null, 
            IAgentEventBroadcaster? broadcaster = null, 
            IAgentRunObserver? observer = null)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
            _router = router;
            _rag = rag;
            _telemetry = telemetry;
            _selfHealing = selfHealing;
            _appState = appState;
            _embedding = embedding;
            _broadcaster = broadcaster;
            _observer = observer ?? NullAgentRunObserver.Instance;
        }

        public static AgentLoop CreateForTest(
            ToolOrchestrator orchestrator, 
            IServiceProvider serviceProvider, 
            IInputBroker broker, 
            ISmartRouter router, 
            IAppState appState,
            IEmbeddingProvider? embedding = null, 
            IAgentEventBroadcaster? broadcaster = null, 
            IAgentRunObserver? observer = null)
        {
            return new AgentLoop(orchestrator, serviceProvider, broker, router, new RAGService(embedding), new TelemetryService(), new Claude4Net.Runtime.Services.SelfHealingService(), appState, embedding, broadcaster, observer);
        }

        private async Task ReportAsync(IAgentRunEvent e)
        {
            try { await _observer.OnEventAsync(e); }
            catch (Exception ex) { AnsiConsole.MarkupLine($"[red]Warning: Observer failed:[/] {Markup.Escape(ex.Message)}"); }
        }

        private async Task EnsureSessionInitializedAsync(string providerName, string modelName)
        {
            if (string.IsNullOrEmpty(AppState.CurrentCwd)) return;
            if (_sessionStore != null && _sessionStore.WorkspaceRoot == AppState.CurrentCwd && _sessionStore.SessionId == AppState.SessionId) return;
            _sessionStore = new AgentSessionStore(AppState.CurrentCwd, AppState.SessionId);
            await _sessionStore.InitializeAsync(new AgentSessionRecord { SessionId = AppState.SessionId, StartTime = DateTime.UtcNow, Provider = providerName, Model = modelName, PermissionMode = AppState.CurrentPermissionMode, WorkspacePath = AppState.CurrentCwd });
            if (_observer is NullAgentRunObserver) AnsiConsole.MarkupLine($"[grey]Session initialized:[/] [link]{_sessionStore.SessionDir}[/]");
            await AppendEventAsync(new SessionStartedEvent { WorkspacePath = AppState.CurrentCwd, Provider = providerName, Model = modelName });
        }

        private async Task SyncTaskBoardAsync()
        {
            if (_sessionStore == null) return;
            var board = new AgentTaskBoardRecord { SessionId = AppState.SessionId, LastUpdatedAt = DateTime.UtcNow, Tasks = AppState.Tasks.Values.Select(t => new AgentTaskRecord { Id = t.Id, Title = t is CoordinateTask ct ? ct.Title : t.Id, Description = t is CoordinateTask ct2 ? ct2.Description : t.Type, Status = t.Status, AssignedAgent = t is CoordinateTask ct3 ? ct3.AssignedAgent : null }).ToList() };
            await _sessionStore.SaveTaskBoardAsync(board);
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
                        string diagnosis = await _telemetry.GenerateReflectionSummaryAsync();
                        if (string.IsNullOrEmpty(diagnosis)) { await context.Output.WriteAsync("No trajectories found."); Console.Write("\n> "); continue; }
                        _selfHealing.UpdateGuide(diagnosis);
                        AnsiConsole.MarkupLine("[bold green]SELF_HEAL_GUIDE.md updated.[/]");
                        finalPrompt = "Diagnosis result: " + diagnosis;
                    }
                    else
                    {
                        string? routedCommand = QueryRouter.Route(context.Text);
                        var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output, context.Approval) : context;
                        if (await HandleSystemCommand(effectiveContext, ct)) { Console.Write("\n> "); continue; }
                        finalPrompt = effectiveContext.Text;
                    }

                    if (string.IsNullOrEmpty(AppState.CurrentCwd)) { await context.Output.WriteAsync("Error: Workspace not set."); Console.Write("\n> "); continue; }

                    var decision = _router.Route(finalPrompt);
                    var providerRegistry = _serviceProvider.GetRequiredService<ProviderRegistry>();
                    ILLMProvider provider = providerRegistry.CreateProvider(decision.SelectedProvider, _serviceProvider);
                    await ReportAsync(new RoutingSelectedEvent(decision.SelectedProvider, decision.SelectedModel, decision.Reason ?? "Auto"));
                    string relevantContext = await _rag.RetrieveRelevantMemoriesAsync(finalPrompt);
                    await RunAsync(relevantContext + finalPrompt, context.Output, provider, decision.SelectedModel, context.Approval, ct);
                    Console.Write("\n> ");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { AnsiConsole.Console.Write(new Markup($"[bold red][[Agent]] Consumer Error:[/] {Markup.Escape(ex.Message)}\n")); }
            }
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
            if (DryRunEngine.IsActive)
            {
                DryRunEngine.Clear();
            }
            var runStartTime = DateTime.UtcNow;
            await ReportAsync(new RunStartedEvent(AppState.SessionId, provider.Name, model, userPrompt));
            _selfHealing.ResetReflectionDepth();
            await EnsureSessionInitializedAsync(provider.Name, model);
            await LogProgressAsync("UserPrompt", message: userPrompt);
            await AppendEventAsync(new UserPromptReceivedEvent { Prompt = userPrompt });

            string currentPrompt = userPrompt;

            string initialGuide = _selfHealing.GetGuide();
            if (!string.IsNullOrEmpty(initialGuide) && !initialGuide.Contains("No active self-healing guidelines"))
            {
                currentPrompt = initialGuide + "\n\n" + userPrompt;
            }

            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 200;

            var sw = Stopwatch.StartNew();
            bool hasError = false;
            string lastTurnResponse = "";

            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                var allEvents = await CurrentEventStore.GetEventsAsync(AppState.SessionId);
                var recentEventsList = allEvents.ToList();
                var pattern = _selfHealing.ClassifyPattern(recentEventsList.TakeLast(10).Cast<object>());
                if (pattern != Claude4Net.Runtime.Services.FailurePattern.None)
                {
                    if (_selfHealing.IncrementReflectionDepth())
                    {
                        var directive = _selfHealing.GenerateDirective(pattern);
                        AnsiConsole.MarkupLine($"[bold yellow]?? Self-Healing Triggered:[/] Detected {pattern}. Injecting directive.");
                        await LogProgressAsync("SelfHealing", message: $"Detected {pattern}", data: directive.Instruction);

                        if (!isFirstTurn)
                        {
                            provider.AddMessage(new { role = "user", content = $"[SELF-HEALING] {directive.Instruction}" });
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[bold red]?? Max Reflection Depth Reached. Switching Strategy...[/]");
                        await LogProgressAsync("StrategySwitch", message: "Max reflection depth reached. Switching strategy.");
                        provider.AddMessage(new { role = "user", content = "CRITICAL: Previous strategy failed repeatedly. Abandon current approach and try a fundamentally different one." });
                    }
                }

                var history = provider.GetHistory();
                int currentTokens = provider.TokenCounter.CountTokens(history);
                int limit = provider.ContextLimit;

                if (currentTokens > limit * 0.8)
                {
                    AnsiConsole.MarkupLine($"[yellow]?? Context limit reached ({currentTokens}/{limit}). Compressing...[/]");
                    var compressedHistory = ContextCompressor.Compress(history.ToList(), provider.TokenCounter, limit);
                    provider.SetHistory(compressedHistory);
                    int compressedTokens = provider.TokenCounter.CountTokens(compressedHistory);
                    AnsiConsole.MarkupLine($"[green]?? Context compressed: {currentTokens} -> {compressedTokens} tokens.[/]");
                    await LogProgressAsync("ContextCompressed", message: $"Compressed from {currentTokens} to {compressedTokens} tokens.");
                }

                turnCount++;
                await ReportAsync(new ThinkingStartedEvent(turnCount));
                var toolCalls = new List<ToolUseRequest>();

                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    if (_observer is NullAgentRunObserver)
                        AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");
                    await LogProgressAsync("ThinkingStart", message: $"Turn {turnCount}");

                    string turnPrompt = isFirstTurn ? currentPrompt : "Proceed based on previous tool results.";

                    if (!isFirstTurn && (provider.Name == "gemini" || provider.Name == "gemini-cli"))
                    {
                        turnPrompt = "";
                    }

                    await foreach (var evt in provider.StreamQueryAsync(turnPrompt, model: model, ct: ct))
                    {
                        if (evt.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(evt.Delta))
                        {
                            if (turnTextBuilder.Length == 0 && _observer is NullAgentRunObserver) Console.WriteLine();
                            if (_observer is NullAgentRunObserver) Console.Write(evt.Delta);
                            turnTextBuilder.Append(evt.Delta);
                            await ReportAsync(new TextDeltaEvent(evt.Delta));
                        }
                        else if (evt.Type == LLMStreamEventType.ThinkingDelta)
                        {
                            if (_observer is NullAgentRunObserver) Console.Write(".");
                            await ReportAsync(new ThinkingDeltaEvent(evt.Delta));
                        }
                        else if (evt.Type == LLMStreamEventType.ToolCallStart && evt.ToolCall != null)
                        {
                            if (_observer is NullAgentRunObserver) Console.Write("!");
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
                    if (_observer is NullAgentRunObserver) Console.WriteLine();

                    if (turnTextBuilder.Length > 0)
                    {
                        lastTurnResponse = turnTextBuilder.ToString();
                        _lastTurnToolCallCount = toolCalls.Count;

                        // If observer is NullAgentRunObserver, we are in legacy mode and need the full output write.
                        // If a real observer is present, deltas were already reported during streaming,
                        // so we skip direct write to avoid duplication in Lumen UI.
                        if (_observer is NullAgentRunObserver)
                        {
                            await output.WriteAsync(lastTurnResponse);
                        }

                        await LogProgressAsync("TextDelta", message: lastTurnResponse);
                        await AppendEventAsync(new AgentThoughtEvent { Thought = lastTurnResponse });
                        await ReportAsync(new AssistantMessageCompletedEvent(lastTurnResponse));
                    }
                }
                catch (Exception ex)
                {
                    hasError = true;
                    string errorMsg = $"Error ({provider.Name}): {ex.Message}";
                    AnsiConsole.Console.Write(new Markup($"\n[bold red]{Markup.Escape(errorMsg)}[/]\n"));
                    await output.WriteAsync(errorMsg);
                    await LogProgressAsync("Error", message: errorMsg);
                    await ReportAsync(new RunErrorEvent(errorMsg));
                    break;
                }

                isFirstTurn = false;

                if (toolCalls.Count > 0)
                {
                    // --- K030: Oscillation Detection ---
                    if (_oscillationDetector.IsOscillating(recentEventsList.TakeLast(10)))
                    {
                        AnsiConsole.MarkupLine("[bold red]?? Oscillation Detected![/] Same tool calls repeating. Intervening...");
                        provider.AddMessage(new { role = "user", content = "SYSTEM: Oscillation detected. You are repeating the same tool calls. Please rethink your strategy and try a different approach." });
                        continue;
                    }

                    foreach (var tc in toolCalls)
                    {
                        if (_observer is NullAgentRunObserver)
                            AnsiConsole.MarkupLine($"[grey]?? [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                        await ReportAsync(new ToolCallQueuedEvent(tc.Id, tc.Name, tc.Input?.ToString() ?? ""));
                        await LogProgressAsync("ToolCall", message: tc.Name, data: tc.Input);
                        await AppendEventAsync(new ToolCalledEvent
                        {
                            ToolUseId = tc.Id,
                            ToolName = tc.Name,
                            Arguments = tc.Input?.ToString() ?? ""
                        });
                    }

                    var batchResults = DryRunEngine.IsActive
                        ? await DryRunEngine.ExecuteSimulatedBatchAsync(toolCalls, _orchestrator, approval, ct)
                        : await _orchestrator.ExecuteBatchAsync(toolCalls, new { }, approval, ct);

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";
                        await ReportAsync(new ToolResultReceivedEvent(result.ToolUseId, result.Content, result.IsError));
                        await LogProgressAsync("ToolResult", message: result.ToolUseId, data: new { result.IsError, result.Content });
                        await AppendEventAsync(new ToolResultEvent
                        {
                            ToolUseId = result.ToolUseId,
                            Result = summary,
                            IsError = result.IsError
                        });

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

                        if (_observer is NullAgentRunObserver)
                        {
                            if (result.IsError)
                                AnsiConsole.MarkupLine($"  [red]?? {escapedId}:[/] [grey]{escapedSummary}[/]");
                            else
                                AnsiConsole.MarkupLine($"  [green]?? {escapedId}:[/] [grey]{escapedSummary}[/]");
                        }

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content ?? "Success", is_error = result.IsError });
                    }

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

                    var processedResults = provider.Name == "gemini" || provider.Name == "gemini-cli"
                        ? toolResults
                        : ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
                    continue;
                }

                break;
            }

            if (!string.IsNullOrEmpty(lastTurnResponse))
            {
                await AppendEventAsync(new FinalResponseGeneratedEvent { Response = lastTurnResponse });
            }

            await output.CompleteAsync(lastTurnResponse);

            sw.Stop();
            _router.UpdateMetric(provider.Name, sw.Elapsed.TotalMilliseconds, hasError);

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

            if (DryRunEngine.IsActive)
            {
                DryRunEngine.RenderReport();
            }

            _lastResponseText = lastTurnResponse;

            await ReportAsync(new RunCompletedEvent(AppState.SessionId, sw.Elapsed));
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

        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            string cmdText = context.Text.Trim();
            if (!cmdText.StartsWith("/") && !cmdText.StartsWith("!"))
                return false;

            string normalizedCmd = cmdText.Substring(1);
            var parts = normalizedCmd.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string cmdName = parts[0].ToLowerInvariant();
            string args = parts.Length > 1 ? parts[1] : string.Empty;

            switch (cmdName)
            {
                case "resume":
                    string resumeResult = await AgentControlCommands.HandleResume(
                        args,
                        _serviceProvider,
                        v => _currentVersion = v,
                        p => _resumedProvider = p);
                    await context.Output.WriteAsync(resumeResult);
                    return true;

                case "replay":
                    string replayResult = await AgentControlCommands.HandleReplay(args, _serviceProvider);
                    await context.Output.WriteAsync(replayResult);
                    return true;

                case "build":
                    string buildResult = await AgentControlCommands.HandleBuild(args, _serviceProvider);
                    await context.Output.WriteAsync(buildResult);
                    return true;

                case "test":
                    string testResult = await AgentControlCommands.HandleTest(args, _serviceProvider);
                    await context.Output.WriteAsync(testResult);
                    return true;

                case "clean":
                    string cleanResult = await AgentControlCommands.HandleClean(args, _serviceProvider);
                    await context.Output.WriteAsync(cleanResult);
                    return true;

                case "clear":
                    string clearResult = await SystemCommands.HandleClear(args, _serviceProvider);
                    await context.Output.WriteAsync(clearResult);
                    return true;

                case "exit":
                case "quit":
                    string exitResult = await SystemCommands.HandleExit(args, _serviceProvider);
                    await context.Output.WriteAsync(exitResult);
                    return true;

                case "tools":
                    string toolsResult = await SystemCommands.HandleTools(args, _serviceProvider);
                    await context.Output.WriteAsync(toolsResult);
                    return true;

                case "reload":
                    string reloadResult = await SystemCommands.HandleReload(args, _serviceProvider);
                    await context.Output.WriteAsync(reloadResult);
                    return true;

                case "prune":
                    string pruneResult = await SystemCommands.HandlePrune(args, _serviceProvider);
                    await context.Output.WriteAsync(pruneResult);
                    return true;

                case "env":
                    string envResult = await SystemCommands.HandleEnv(args, _serviceProvider);
                    await context.Output.WriteAsync(envResult);
                    return true;

                case "whoami":
                    string whoamiResult = await SystemCommands.HandleWhoAmI(args, _serviceProvider);
                    await context.Output.WriteAsync(whoamiResult);
                    return true;

                case "status":
                    string statusResult = await SystemCommands.HandleStatus(args, _serviceProvider);
                    await context.Output.WriteAsync(statusResult);
                    return true;

                case "usage":
                    string usageResult = await SystemCommands.HandleUsage(args, _serviceProvider);
                    await context.Output.WriteAsync(usageResult);
                    return true;

                case "api":
                    string apiResult = await SystemCommands.HandleApi(args, _serviceProvider);
                    await context.Output.WriteAsync(apiResult);
                    return true;

                case "login":
                    string loginResult = await ProviderCommands.HandleLogin(args, _serviceProvider);
                    await context.Output.WriteAsync(loginResult);
                    return true;

                case "model":
                    string modelResult = await ProviderCommands.HandleModel(args, _serviceProvider);
                    await context.Output.WriteAsync(modelResult);
                    return true;

                case "reset":
                    string resetResult = await ProviderCommands.HandleReset(args, _serviceProvider);
                    await context.Output.WriteAsync(resetResult);
                    return true;

                case "ls":
                    string lsResult = await FileCommands.HandleLs(args, _serviceProvider);
                    await context.Output.WriteAsync(lsResult);
                    return true;

                case "pwd":
                    string pwdResult = await FileCommands.HandlePwd(args, _serviceProvider);
                    await context.Output.WriteAsync(pwdResult);
                    return true;

                case "setworkspace":
                    string wsResult = await FileCommands.HandleSetWorkspace(args, _serviceProvider);
                    await context.Output.WriteAsync(wsResult);
                    return true;

                case "cd":
                    string cdResult = await FileCommands.HandleCd(args, _serviceProvider);
                    await context.Output.WriteAsync(cdResult);
                    return true;

                case "goal":
                    string goalResult = await AgentGoalCommands.HandleGoal(args, _serviceProvider);
                    await context.Output.WriteAsync(goalResult);
                    return true;

                case "coordinate":
                    string coordResult = await AgentGoalCommands.HandleCoordinate(args, _serviceProvider);
                    await context.Output.WriteAsync(coordResult);
                    return true;

                case "routine":
                    string routineResult = await AgentGoalCommands.HandleRoutine(args, _serviceProvider);
                    await context.Output.WriteAsync(routineResult);
                    return true;

                case "handoff":
                    string handoffResult = await AgentGoalCommands.HandleHandoff(args, _serviceProvider);
                    await context.Output.WriteAsync(handoffResult);
                    return true;

                case "checkpoint":
                    string checkpointResult = await AgentGoalCommands.HandleCheckpoint(args, _serviceProvider);
                    await context.Output.WriteAsync(checkpointResult);
                    return true;

                case "spec":
                    string specResult = await SpecVerifyCommands.HandleSpec(args, _serviceProvider);
                    await context.Output.WriteAsync(specResult);
                    return true;

                case "verify":
                    string verifyResult = await SpecVerifyCommands.HandleVerify(args, _serviceProvider);
                    await context.Output.WriteAsync(verifyResult);
                    return true;

                case "skills":
                    string skillsResult = await SpecVerifyCommands.HandleSkills(args, _serviceProvider);
                    await context.Output.WriteAsync(skillsResult);
                    return true;

                case "skill-proposals":
                    string spResult = await SpecVerifyCommands.HandleSkillProposals(args, _serviceProvider);
                    await context.Output.WriteAsync(spResult);
                    return true;

                case "skill-propose":
                    string sprResult = await SpecVerifyCommands.HandleSkillPropose(args, _serviceProvider);
                    await context.Output.WriteAsync(sprResult);
                    return true;

                case "skill":
                    string skillResult = await SpecVerifyCommands.HandleSkill(args, _serviceProvider);
                    await context.Output.WriteAsync(skillResult);
                    return true;

                case "save":
                    try
                    {
                        if (string.IsNullOrEmpty(AppState.CurrentCwd))
                        {
                            await context.Output.WriteAsync("Error: Workspace is not set.");
                            return true;
                        }
                        var registry = _serviceProvider.GetRequiredService<ProviderRegistry>();
                        var provider = registry.CreateProvider(AppState.ActiveProvider, _serviceProvider);
                        var history = provider.GetHistory();
                        string dateStr = DateTime.Now.ToString("yyyyMMdd");
                        string fileName = $"context_{dateStr}.json";
                        string fullPath = Path.Combine(AppState.CurrentCwd, fileName);
                        string json = JsonSerializer.Serialize(history, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(fullPath, json);
                        await context.Output.WriteAsync($"Conversation context saved to {fileName}");
                    }
                    catch (Exception ex)
                    {
                        await context.Output.WriteAsync($"Error saving context: {ex.Message}");
                    }
                    return true;
            }

            return false;
        }
    }
}
