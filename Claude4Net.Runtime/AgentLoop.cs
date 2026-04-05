using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using Spectre.Console;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class AgentLoop
    {
        private readonly ILLMProvider _provider;
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;

        public AgentLoop(ILLMProvider provider, ToolOrchestrator orchestrator, IServiceProvider serviceProvider)
        {
            _provider = provider;
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
        }

        public async Task RunAsync(string userPrompt, CancellationToken ct = default)
        {
            string currentPrompt = userPrompt;
            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 100; // Increased for deeper analysis

            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                turnCount++;
                var toolCalls = new List<ToolUseRequest>();

                try
                {
                    // Show both Thinking and Output
                    await AnsiConsole.Live(new Panel("Initializing...") { Header = new PanelHeader($"{_provider.Name.ToUpper()} (Turn {turnCount})"), Border = BoxBorder.Rounded })
                        .StartAsync(async ctx =>
                        {
                            var textBuilder = new System.Text.StringBuilder();
                            var thinkingBuilder = new System.Text.StringBuilder();

                            await foreach (var evt in _provider.StreamQueryAsync(isFirstTurn ? currentPrompt : "Proceed based on previous tool results. Continue analysis.", model: AppState.ActiveModel, ct: ct))
                            {
                                if (evt.Type == LLMStreamEventType.ThinkingDelta)
                                {
                                    thinkingBuilder.Append(evt.Delta);
                                    ctx.UpdateTarget(new Panel(new Markup($"[grey]{Markup.Escape(thinkingBuilder.ToString())}[/]")) { Header = new PanelHeader("Thinking Process"), Border = BoxBorder.Rounded });
                                }
                                else if (evt.Type == LLMStreamEventType.TextDelta)
                                {
                                    textBuilder.Append(evt.Delta);
                                    ctx.UpdateTarget(new Panel(textBuilder.ToString()) { Header = new PanelHeader($"{_provider.Name.ToUpper()} Response"), Border = BoxBorder.Rounded });
                                }
                                else if (evt.Type == LLMStreamEventType.ToolCallStart && evt.ToolCall != null)
                                {
                                    toolCalls.Add(evt.ToolCall);
                                }
                                else if (evt.Type == LLMStreamEventType.Completed && evt.FinalResponse != null)
                                {
                                    foreach (var tc in evt.FinalResponse.ToolCalls)
                                    {
                                        if (!toolCalls.Any(existing => existing.Name == tc.Name)) toolCalls.Add(tc);
                                    }
                                }
                            }
                        });
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"\n[bold red]Error ({_provider.Name}):[/] {Markup.Escape(ex.Message)}");
                    break;
                }

                isFirstTurn = false;

                if (toolCalls.Count > 0)
                {
                    AnsiConsole.MarkupLine($"\n[bold yellow]🛠️  Executing {toolCalls.Count} tools...[/]");
                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { });

                    var toolResults = new List<object>();
                    foreach (var result in batchResults)
                    {
                        string summary = result.Content?.ToString() ?? "Success";
                        if (summary.Length > 100) summary = summary.Substring(0, 97) + "...";

                        if (result.IsError)
                            AnsiConsole.MarkupLine($"  [red]✗ {result.ToolUseId}:[/] [grey]{Markup.Escape(summary)}[/]");
                        else
                            AnsiConsole.MarkupLine($"  [green]✓ {result.ToolUseId}:[/] [grey]{Markup.Escape(summary)}[/]");

                        toolResults.Add(new { type = "tool_result", tool_use_id = result.ToolUseId, content = result.Content?.ToString() ?? "Success", is_error = result.IsError });
                    }

                    _provider.AddMessage(new { role = "user", content = toolResults });
                    continue;
                }

                break;
            }

            if (turnCount >= MAX_TURNS)
            {
                AnsiConsole.MarkupLine("\n[bold red]🛑 Circuit Breaker Hit![/] Reached maximum analysis depth (20 turns).");
            }
        }
    }
}
