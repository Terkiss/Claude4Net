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

namespace Claude4Net.Runtime
{
    public class AgentLoop
    {
        private readonly ToolOrchestrator _orchestrator;
        private readonly IServiceProvider _serviceProvider;
        private readonly IInputBroker _broker;

        public AgentLoop(ToolOrchestrator orchestrator, IServiceProvider serviceProvider, IInputBroker broker)
        {
            _orchestrator = orchestrator;
            _serviceProvider = serviceProvider;
            _broker = broker;
        }

        public async Task ListenAsync(CancellationToken ct = default)
        {
            AnsiConsole.MarkupLine("[bold cyan][[Agent]][/] Consumer loop started. Waiting for messages...");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var context = await _broker.ReadAsync(ct);
                    if (string.IsNullOrWhiteSpace(context.Text)) continue;

                    // Resolve current active provider dynamically for every message
                    ILLMProvider provider;
                    if (AppState.ActiveProvider == "gemini") 
                        provider = _serviceProvider.GetRequiredService<GeminiProvider>();
                    else if (AppState.ActiveProvider == "ollama") 
                        provider = _serviceProvider.GetRequiredService<OllamaProvider>();
                    else 
                        provider = _serviceProvider.GetRequiredService<ClaudeService>();

                    await RunAsync(context.Text, context.Output, provider, ct);
                    
                    Console.Write("\n> ");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    AnsiConsole.Console.Write(new Markup($"[bold red][[Agent]] Consumer Error:[/] {Markup.Escape(ex.Message)}\n"));
                }
            }
        }

        public async Task RunAsync(string userPrompt, IOutputHandler output, ILLMProvider provider, CancellationToken ct = default)
        {
            string currentPrompt = userPrompt;
            bool isFirstTurn = true;
            int turnCount = 0;
            const int MAX_TURNS = 100; 

            while (!ct.IsCancellationRequested && turnCount < MAX_TURNS)
            {
                turnCount++;
                var toolCalls = new List<ToolUseRequest>();
                var turnTextBuilder = new System.Text.StringBuilder();

                try
                {
                    string providerName = Markup.Escape(provider.Name);
                    AnsiConsole.Markup($"[grey]Thinking... ({providerName} T{turnCount}) [/]");
                    
                    await foreach (var evt in provider.StreamQueryAsync(isFirstTurn ? currentPrompt : "Proceed based on previous tool results.", model: AppState.ActiveModel, ct: ct))
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
                                if (!toolCalls.Any(existing => existing.Name == tc.Name)) toolCalls.Add(tc);
                            }
                        }
                    }
                    Console.WriteLine();

                    if (turnTextBuilder.Length > 0)
                    {
                        await output.WriteAsync(turnTextBuilder.ToString());
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Error ({provider.Name}): {ex.Message}";
                    AnsiConsole.Console.Write(new Markup($"\n[bold red]{Markup.Escape(errorMsg)}[/]\n"));
                    await output.WriteAsync(errorMsg);
                    break;
                }

                isFirstTurn = false;

                if (toolCalls.Count > 0)
                {
                    foreach(var tc in toolCalls)
                    {
                        AnsiConsole.MarkupLine($"[grey]🛠️  [bold yellow]Tool Call:[/] {Markup.Escape(tc.Name)}[/]");
                    }
                    
                    var batchResults = await _orchestrator.ExecuteBatchAsync(toolCalls, new { });

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

                    provider.AddMessage(new { role = "user", content = toolResults });
                    continue;
                }

                break;
            }

            if (turnCount >= MAX_TURNS)
            {
                AnsiConsole.MarkupLine("\n[bold red]🛑 Circuit Breaker Hit![/]");
            }
        }
    }
}
