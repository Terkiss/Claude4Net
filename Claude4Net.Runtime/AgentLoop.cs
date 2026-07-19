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
            return new AgentLoop(orchestrator, serviceProvider, broker, router, new RAGService(embedding), new TelemetryService(), new SelfHealingService(), appState, embedding, broadcaster, observer);
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

        private async Task AppendEventAsync(IAgentEvent @event)
        {
            if (@event is AgentEventBase baseEv) baseEv.Version = ++_currentVersion;
            await CurrentEventStore.AppendEventAsync(AppState.SessionId, @event);
            if (_broadcaster != null) await _broadcaster.BroadcastAsync(@event);
        }

        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, string model, IUserApprovalHandler? approval = null, CancellationToken ct = default)
        {
            await EnsureSessionInitializedAsync(provider.Name, model);
            await AppendEventAsync(new UserPromptReceivedEvent { Prompt = userPrompt });
            string currentPrompt = userPrompt;
            string initialGuide = _selfHealing.GetGuide();
            if (!string.IsNullOrEmpty(initialGuide)) currentPrompt = initialGuide + "\n\n" + userPrompt;

            int turnCount = 0;
            while (!ct.IsCancellationRequested && turnCount < 200)
            {
                var allEvents = await CurrentEventStore.GetEventsAsync(AppState.SessionId);
                var recentEventsList = allEvents.ToList();
                var pattern = _selfHealing.ClassifyPattern(recentEventsList.TakeLast(10).Cast<object>());
                if (pattern != Claude4Net.Runtime.Services.FailurePattern.None)
                {
                    if (_selfHealing.IncrementReflectionDepth())
                    {
                        var directive = _selfHealing.GenerateDirective(pattern);
                        provider.AddMessage(new { role = "user", content = $"[SELF-HEALING] {directive.Instruction}" });
                    }
                }
                turnCount++;
                // ...
            }
        }

        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            return true;
        }
    }
}
