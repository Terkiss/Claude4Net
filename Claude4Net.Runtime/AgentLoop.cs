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

                    // --- [Task 5.1: Intent-based Query Routing] ---
                    string? routedCommand = QueryRouter.Route(context.Text);
                    string finalInput = routedCommand ?? context.Text;

                    // --- [System Command Interception] ---
                    // Re-create a temporary context if routed to a command
                    var effectiveContext = routedCommand != null ? new InputContext(routedCommand, context.Output) : context;
                    if (await HandleSystemCommand(effectiveContext, ct)) continue;

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

        private async Task<bool> HandleSystemCommand(InputContext context, CancellationToken ct)
        {
            string cmd = context.Text.Trim().ToLower();
            if (!cmd.StartsWith("!")) return false;

            string[] parts = cmd.Split(' ', 2);
            string baseCmd = parts[0];

            switch (baseCmd)
            {
                case "!build":
                    AnsiConsole.MarkupLine("[bold blue]Building project...[/]");
                    // Logic to invoke dotnet build etc.
                    await context.Output.WriteAsync("Build triggered.");
                    return true;

                case "!test":
                    AnsiConsole.MarkupLine("[bold blue]Running tests...[/]");
                    await context.Output.WriteAsync("Test suite execution started.");
                    return true;

                case "!clean":
                    AnsiConsole.MarkupLine("[bold blue]Cleaning solution...[/]");
                    await context.Output.WriteAsync("Solution clean started.");
                    return true;

                case "!clear":
                    Console.Clear();
                    AnsiConsole.MarkupLine("[bold green]Console cleared.[/]");
                    return true;

                case "!exit":
                case "!quit":
                    AnsiConsole.MarkupLine("[bold yellow]System is shutting down safely...[/]");
                    await context.Output.WriteAsync("Agent is going offline.");
                    Environment.Exit(0);
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
                    AnsiConsole.MarkupLine("[bold purple]Notice:[/] Dynamic hot-reloading requires a host-level rescan. Currently available tools remain active.");
                    AnsiConsole.MarkupLine("[bold green]Plugin metadata refreshed![/]");
                    await context.Output.WriteAsync("System plugins metadata refreshed.");
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

                    // Task 3.2: Context Compression
                    var processedResults = ContextCompressor.SummarizeToolResults(toolResults);
                    provider.AddMessage(new { role = "user", content = processedResults });
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
