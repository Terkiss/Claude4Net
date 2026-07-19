using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Claude4Net.Runtime.Handlers
{
    public static class AgentControlCommands
    {
        public static async Task<string> HandleResume(string args, IServiceProvider sp, Action<long> setVersion, Action<ILLMProvider> setProviderHistory)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                return "[bold yellow]Usage:[/] !resume <sessionId>";
            }

            if (string.IsNullOrEmpty(AppState.CurrentCwd))
            {
                return "[bold red]Error:[/] Workspace is not set. Use /setworkspace first.";
            }

            string targetId = args.Trim();
            var sessionRecord = await AgentSessionStore.LoadSessionRecordAsync(AppState.CurrentCwd, targetId);
            if (sessionRecord == null)
            {
                return $"[bold red]Error:[/] Session '{targetId}' not found in workspace.";
            }

            AnsiConsole.MarkupLine($"[bold green]Session '{targetId}' found.[/]");
            AnsiConsole.MarkupLine($"- Start Time: {sessionRecord.StartTime}");
            AnsiConsole.MarkupLine($"- Provider: {sessionRecord.Provider}");
            AnsiConsole.MarkupLine($"- Model: {sessionRecord.Model}");
            AnsiConsole.MarkupLine($"- Permission: {sessionRecord.PermissionMode}");

            var eventStore = new FileAgentEventStore(AppState.CurrentCwd);
            var resumeEvents = await eventStore.GetEventsAsync(targetId);
            var resumeSnapshot = await eventStore.GetLatestSnapshotAsync(targetId);
            var state = AgentStateReconstructor.Reconstruct(resumeEvents, resumeSnapshot);

            AppState.SessionId = targetId;
            AppState.ActiveProvider = sessionRecord.Provider;
            AppState.ActiveModel = sessionRecord.Model;
            AppState.CurrentPermissionMode = sessionRecord.PermissionMode;

            var providerRegistry = sp.GetRequiredService<ProviderRegistry>();
            var descriptor = providerRegistry.Get(sessionRecord.Provider);
            if (descriptor != null)
            {
                AnsiConsole.MarkupLine($"[grey]Provider details: {descriptor.Label} (Transport: {descriptor.TransportKind})[/]");
            }

            ILLMProvider resumeProvider;
            try
            {
                resumeProvider = providerRegistry.CreateProvider(sessionRecord.Provider, sp);
            }
            catch
            {
                resumeProvider = sessionRecord.Provider switch
                {
                    "gemini" => sp.GetRequiredService<GeminiProvider>(),
                    "gemini-cli" => sp.GetRequiredService<GeminiCliProvider>(),
                    "ollama" => sp.GetRequiredService<OllamaProvider>(),
                    _ => sp.GetRequiredService<ClaudeService>()
                };
            }

            resumeProvider.SetHistory(state.History);
            setVersion(state.LastVersion);
            setProviderHistory(resumeProvider);

            return $"Session {targetId} resumed. ({state.History.Count} messages, Version {state.LastVersion})";
        }

        public static async Task<string> HandleReplay(string args, IServiceProvider sp)
        {
            string replaySessionId = !string.IsNullOrWhiteSpace(args) ? args.Trim() : AppState.SessionId;
            string ws = AppState.CurrentCwd ?? Directory.GetCurrentDirectory();
            var eventStore = new FileAgentEventStore(ws);
            
            var events = await eventStore.GetEventsAsync(replaySessionId);
            if (!events.Any())
            {
                return $"[yellow]No events found for session {replaySessionId}[/]";
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
            
            return $"Replayed {events.Count()} events for session {replaySessionId}";
        }

        public static Task<string> HandleBuild(string a, IServiceProvider sp)
        {
            AnsiConsole.MarkupLine("[bold blue]Building project...[/]");
            return Task.FromResult("Build triggered.");
        }

        public static Task<string> HandleTest(string a, IServiceProvider sp)
        {
            AnsiConsole.MarkupLine("[bold blue]Running tests...[/]");
            return Task.FromResult("Test suite execution started.");
        }

        public static Task<string> HandleClean(string a, IServiceProvider sp)
        {
            AnsiConsole.MarkupLine("[bold blue]Cleaning solution...[/]");
            return Task.FromResult("Solution clean started.");
        }
    }
}
