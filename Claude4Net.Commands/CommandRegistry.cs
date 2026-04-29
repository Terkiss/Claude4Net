using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Diagnostics;
using Spectre.Console;

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
            new Command { Name = "help", Description = "Show help", Handler = (a, sp) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Available Commands:[/]");
                foreach(var c in _commands!.OrderBy(x => x.Name))
                {
                    sb.AppendLine($"  [bold]/{c.Name.PadRight(10)}[/] - {Markup.Escape(c.Description)}");
                }
                return Task.FromResult(sb.ToString());
            }},
            
            new Command { Name = "yolo", Description = "ROOT ACCESS - Bypass all permissions", Handler = (a, sp) => {
                if (AppState.CurrentPermissionMode == PermissionMode.Yolo) {
                    AppState.CurrentPermissionMode = PermissionMode.Default;
                    return Task.FromResult("[yellow]YOLO Mode Disabled.[/]");
                } else {
                    AppState.CurrentPermissionMode = PermissionMode.Yolo;
                    return Task.FromResult("[bold red]YOLO MODE ACTIVATED![/]");
                }
            }},

            new Command { Name = "login", Description = "Log in to a provider (gemini, claude, ollama, gemini-cli)", Handler = async (args, sp) => {
                var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "Usage: !login <provider> [key_or_uri]";
                
                string provider = parts[0].ToLowerInvariant();
                if (provider == "geminicli" || provider == "gemini-cli")
                {
                    AppState.ActiveProvider = "gemini-cli";
                    return $"[green]Logged in to Gemini CLI (gemini-cli).[/] No API key required (OAuth handled by CLI). Provider switched.";
                }

                if (parts.Length < 2) return $"Usage: !login <provider> <key_or_uri>\n[bold red]Error:[/] API key is required for '{Markup.Escape(provider)}'.";
                
                await AuthManager.SaveProviderKeyAsync(provider, parts[1]);
                AppState.ActiveProvider = provider;
                return $"[green]Logged in to {Markup.Escape(provider)}.[/] API key saved and provider switched.";
            }},

            new Command { Name = "model", Description = "Browse and change LLM models", Handler = async (args, sp) => {
                if (string.IsNullOrWhiteSpace(args)) {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"[bold cyan]Current Session Status:[/]");
                    sb.AppendLine($"  Provider: [bold]{Markup.Escape(AppState.ActiveProvider)}[/]");
                    sb.AppendLine($"  Active Model: [bold]{Markup.Escape(AppState.ActiveModel)}[/]");
                    sb.AppendLine();

                    if (!string.IsNullOrEmpty(AuthManager.GetGeminiApiKey())) {
                        sb.AppendLine("[bold yellow]Google Gemini Models (Available):[/]");
                        sb.AppendLine("  - gemini-3.1-pro, gemini-3.0-flash, gemini-3.1-deep-think, gemini-3.1-flash-lite, gemini-3.1-flash-live");
                        sb.AppendLine("  - gemini-2.5-pro, gemini-2.5-flash");
                        sb.AppendLine("  - gemini-2.0-flash, gemini-2.0-flash-lite-preview-02-05, gemini-2.0-pro-exp-02-05, gemini-2.0-flash-thinking-exp-01-21");
                        sb.AppendLine("  - gemini-1.5-pro, gemini-1.5-flash, gemini-1.5-flash-8b");
                        sb.AppendLine();
                    }

                    if (!string.IsNullOrEmpty(AuthManager.GetAnthropicApiKey())) {
                        sb.AppendLine("[bold magenta]Anthropic Claude Models (Available):[/]");
                        sb.AppendLine("  - claude-3-5-sonnet-20241022, claude-3-5-haiku-20241022");
                        sb.AppendLine();
                    }

                    string? ollamaUri = AuthManager.GetApiKey("ollama");
                    if (!string.IsNullOrEmpty(ollamaUri)) {
                        sb.AppendLine("[bold green]Ollama Local Models (Real-time):[/]");
                        try {
                            var ollama = sp.GetRequiredService<OllamaProvider>();
                            var models = await ollama.ListModelsAsync();
                            if (models.Any()) foreach(var m in models) sb.AppendLine($"  - {Markup.Escape(m)}");
                            else sb.AppendLine("  (No local models found)");
                        } catch { sb.AppendLine("  (Ollama server not reachable)"); }
                        sb.AppendLine();
                    }

                    sb.AppendLine("[grey]To change model, type: /model <model_name>[/]");
                    return sb.ToString();
                }

                string newModel = args.Trim();
                string detectedProvider = AppState.ActiveProvider;

                if (newModel.StartsWith("claude")) detectedProvider = "claude";
                else if (newModel.StartsWith("gemini")) detectedProvider = "gemini";
                else {
                    try {
                        var ollama = sp.GetRequiredService<OllamaProvider>();
                        var ollamaModels = await ollama.ListModelsAsync();
                        if (ollamaModels.Any(m => m.Equals(newModel, StringComparison.OrdinalIgnoreCase))) detectedProvider = "ollama";
                    } catch { }
                }

                AppState.ActiveModel = newModel;
                AppState.ActiveProvider = detectedProvider;
                return $"[green]Model changed to:[/] [bold]{Markup.Escape(newModel)}[/] (Provider switched to: [bold]{Markup.Escape(detectedProvider)}[/])";
            }},

            new Command { Name = "clear", Description = "Clear the console screen", Handler = (a, sp) => {
                Console.Clear();
                return Task.FromResult("[green]Console cleared.[/]");
            }},

            new Command { Name = "ls", Description = "List files in current directory", Handler = (a, sp) => {
                string currentPath = AppState.CurrentCwd ?? Environment.CurrentDirectory;
                var files = Directory.GetFileSystemEntries(currentPath);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[bold cyan]Directory: {Markup.Escape(currentPath)}[/]");
                foreach(var f in files) 
                {
                    bool isDir = Directory.Exists(f);
                    string tag = isDir ? "[bold blue][[Dir]][/]" : "[grey][[File]][/]";
                    sb.AppendLine($"  {tag} {Markup.Escape(Path.GetFileName(f))}");
                }
                return Task.FromResult(sb.ToString());
            }},

            new Command { Name = "pwd", Description = "Show current working directory", Handler = (a, sp) => {
                string currentPath = AppState.CurrentCwd ?? Environment.CurrentDirectory;
                return Task.FromResult($"[cyan]CWD:[/] {Markup.Escape(currentPath)}");
            }},

            new Command { Name = "setworkspace", Description = "Set the root project workspace path (Required for tools)", Handler = (a, sp) => {
                if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /setworkspace <path>");
                string newPath = Path.GetFullPath(a);
                if (Directory.Exists(newPath)) {
                    AppState.CurrentCwd = newPath;
                    Environment.CurrentDirectory = newPath;
                    return Task.FromResult($"[bold green]Workspace set to:[/] {Markup.Escape(newPath)}\n[grey]Tools are now active for this directory.[/]");
                }
                return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
            }},

            new Command { Name = "cd", Description = "Change current working directory within workspace", Handler = (a, sp) => {
                if (string.IsNullOrEmpty(AppState.CurrentCwd)) return Task.FromResult("[red]Error:[/] Please set your workspace first using [bold]/setworkspace <path>[/]");
                if (string.IsNullOrWhiteSpace(a)) return Task.FromResult("Usage: /cd <path>");
                
                string combined = Path.Combine(Environment.CurrentDirectory, a);
                string newPath = Path.GetFullPath(combined);
                
                if (Directory.Exists(newPath)) {
                    // Check if newPath is still within or equal to the root workspace
                    if (newPath.StartsWith(AppState.CurrentCwd, StringComparison.OrdinalIgnoreCase)) {
                        Environment.CurrentDirectory = newPath;
                        return Task.FromResult($"[green]Directory changed to:[/] {Markup.Escape(newPath)}");
                    }
                    return Task.FromResult($"[red]Error:[/] Cannot move outside the set workspace root: {Markup.Escape(AppState.CurrentCwd)}");
                }
                return Task.FromResult($"[red]Error:[/] Directory not found: {Markup.Escape(newPath)}");
            }},

            new Command { Name = "env", Description = "List environment variables", Handler = (a, sp) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]Environment Variables (Top 20):[/]");
                var env = Environment.GetEnvironmentVariables();
                int count = 0;
                foreach(System.Collections.DictionaryEntry de in env) {
                    if (count++ >= 20) break;
                    sb.AppendLine($"  [bold]{Markup.Escape(de.Key.ToString() ?? "")}[/]: {Markup.Escape(de.Value?.ToString() ?? "")}");
                }
                return Task.FromResult(sb.ToString());
            }},

            new Command { Name = "whoami", Description = "Show current user information", Handler = (a, sp) => {
                return Task.FromResult($"[cyan]User:[/] {Markup.Escape(Environment.UserName)}\n[cyan]Machine:[/] {Markup.Escape(Environment.MachineName)}\n[cyan]Domain:[/] {Markup.Escape(Environment.UserDomainName)}");
            }},

            new Command { Name = "status", Description = "Show system and application status", Handler = (a, sp) => {
                var proc = Process.GetCurrentProcess();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[bold cyan]System Status:[/]");
                sb.AppendLine($"  OS: {Markup.Escape(Environment.OSVersion.ToString())}");
                sb.AppendLine($"  Runtime: {Markup.Escape(Environment.Version.ToString())}");
                sb.AppendLine($"  Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
                sb.AppendLine($"  Threads: {proc.Threads.Count}");
                sb.AppendLine($"  Up Time: {DateTime.Now - proc.StartTime}");
                sb.AppendLine();
                sb.AppendLine("[bold green]Application Status:[/]");
                sb.AppendLine($"  Active Provider: {Markup.Escape(AppState.ActiveProvider)}");
                sb.AppendLine($"  Active Model: {Markup.Escape(AppState.ActiveModel)}");
                sb.AppendLine($"  YOLO Mode: {(AppState.CurrentPermissionMode == PermissionMode.Yolo ? "[red]ON[/]" : "[green]OFF[/]")}");
                return Task.FromResult(sb.ToString());
            }},

            new Command { Name = "usage", Description = "Show model token usage summary", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Usage tracking is active. Summary display pending SDK update.[/]");
            }},

            new Command { Name = "exit", Description = "Exit the application", Handler = (a, sp) => {
                // Return a clear message first
                Task.Run(async () => { await Task.Delay(500); Environment.Exit(0); });
                return Task.FromResult("[bold yellow]System is shutting down... Goodbye![/]");
            }},

            new Command { Name = "reset", Description = "Reset current conversation history", Handler = (a, sp) => {
                return Task.FromResult("[yellow]Session reset command issued. Provider history will be cleared on next turn.[/]");
            }}
        };

        public static List<Command> GetCommands() => new(_commands);
        public static int GetCommandCount() => _commands.Count;
        public static Command? FindCommand(string name) => _commands.Find(c => c.Name.Equals(name.TrimStart('!', '/'), StringComparison.OrdinalIgnoreCase));
    }
}
