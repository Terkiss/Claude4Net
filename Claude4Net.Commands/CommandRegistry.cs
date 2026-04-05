using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Commands
{
    public class Command
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Func<string, IServiceProvider, Task<string>>? Handler { get; set; }
    }

    public static class CommandRegistry
    {
        private static readonly List<Command> _commands = new()
        {
            new Command { Name = "help", Description = "Show help", Handler = (a, sp) => Task.FromResult("Available commands: /help, !yolo, !login <provider> <key_or_uri>, /model <name>") },
            
            new Command { Name = "yolo", Description = "ROOT ACCESS", Handler = (a, sp) => {
                if (AppState.CurrentPermissionMode == PermissionMode.Yolo) {
                    AppState.CurrentPermissionMode = PermissionMode.Default;
                    return Task.FromResult("[yellow]YOLO Mode Disabled.[/]");
                } else {
                    AppState.CurrentPermissionMode = PermissionMode.Yolo;
                    return Task.FromResult("[bold red]YOLO MODE ACTIVATED![/]");
                }
            }},

            new Command { Name = "login", Description = "Log in", Handler = async (args, sp) => {
                var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) return "Usage: !login <provider> <key_or_uri>";
                string provider = parts[0].ToLower();
                await AuthManager.SaveProviderKeyAsync(provider, parts[1]);
                AppState.ActiveProvider = provider;
                return $"[green]Logged in to {provider}.[/] API key saved and provider switched.";
            }},

            new Command { Name = "model", Description = "Browse and change LLM models", Handler = async (args, sp) => {
                if (string.IsNullOrWhiteSpace(args)) {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[bold cyan]Current Session Status:[/]");
                    sb.AppendLine($"  Provider: [bold]{AppState.ActiveProvider}[/]");
                    sb.AppendLine($"  Active Model: [bold]{AppState.ActiveModel}[/]");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(AuthManager.GetGeminiApiKey())) {
                        sb.AppendLine("[bold yellow]Google Gemini Models (Available):[/]");
                        sb.AppendLine("  - gemini-3-flash-preview, gemini-3.1-pro-preview, gemini-2.5-pro, etc.");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrEmpty(AuthManager.GetAnthropicApiKey())) {
                        sb.AppendLine("[bold magenta]Anthropic Claude Models (Available):[/]");
                        sb.AppendLine("  - claude-3-5-sonnet-20241022, claude-3-5-haiku-20241022, etc.");
                        sb.AppendLine();
                    }

                    string? ollamaUri = AuthManager.GetApiKey("ollama");
                    if (!string.IsNullOrEmpty(ollamaUri)) {
                        sb.AppendLine("[bold green]Ollama Local Models (Real-time):[/]");
                        try {
                            var ollama = sp.GetRequiredService<OllamaProvider>();
                            var models = await ollama.ListModelsAsync();
                            if (models.Any()) foreach(var m in models) sb.AppendLine($"  - {m}");
                            else sb.AppendLine("  (No local models found)");
                        } catch { sb.AppendLine("  (Ollama server not reachable)"); }
                        sb.AppendLine();
                    }

                    sb.AppendLine("[grey]To change model, type: /model <model_name>[/]");
                    return sb.ToString();
                }

                string newModel = args.Trim();
                string oldModel = AppState.ActiveModel;
                
                // --- Smart Provider Switching ---
                string detectedProvider = AppState.ActiveProvider;

                if (newModel.StartsWith("claude")) {
                    detectedProvider = "claude";
                } else if (newModel.StartsWith("gemini")) {
                    detectedProvider = "gemini";
                } else {
                    // Check if it's an Ollama model
                    try {
                        var ollama = sp.GetRequiredService<OllamaProvider>();
                        var ollamaModels = await ollama.ListModelsAsync();
                        if (ollamaModels.Any(m => m.Equals(newModel, StringComparison.OrdinalIgnoreCase))) {
                            detectedProvider = "ollama";
                        }
                    } catch { /* Ollama offline, skip check */ }
                }

                AppState.ActiveModel = newModel;
                AppState.ActiveProvider = detectedProvider;
                
                return $"[green]Model changed to:[/] [bold]{newModel}[/] (Provider switched to: [bold]{detectedProvider}[/])";
            }}
        };

        public static List<Command> GetCommands() => new(_commands);
        public static Command? FindCommand(string name) => _commands.Find(c => c.Name.Equals(name.TrimStart('!', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
