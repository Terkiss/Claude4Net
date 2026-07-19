using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Claude4Net.Runtime.Handlers
{
    public static class ProviderCommands
    {
        public static async Task<string> HandleLogin(string args, IServiceProvider sp)
        {
            var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Usage: !login <provider> <key_or_uri>";

            string provider = parts[0].ToLowerInvariant();
            if (provider == "geminicli" || provider == "gemini-cli")
            {
                AppState.ActiveProvider = "gemini-cli";
                AppState.IsProviderExplicitlySet = true;
                return $"[green]Logged in to Gemini CLI (gemini-cli).[/] No API key required (OAuth handled by CLI). Provider switched.";
            }

            // 기존 키 존재 여부 확인 및 자동 전환
            if (parts.Length < 2)
            {
                string? existingKey = AuthManager.GetApiKey(provider);
                if (!string.IsNullOrEmpty(existingKey))
                {
                    AppState.ActiveProvider = provider;
                    AppState.IsProviderExplicitlySet = true;
                    return $"[green]기존 키를 사용하여 {Markup.Escape(provider)}로 전환했습니다.[/]";
                }
                return $"Usage: !login <provider> <key_or_uri>\n[bold red]Error:[/] API key is required for '{Markup.Escape(provider)}'.";
            }

            await AuthManager.SaveProviderKeyAsync(provider, parts[1]);
            AppState.ActiveProvider = provider;
            AppState.IsProviderExplicitlySet = true;
            return $"[green]Logged in to {Markup.Escape(provider)}.[/] API key saved and provider switched.";
        }

        public static async Task<string> HandleModel(string args, IServiceProvider sp)
        {
            if (string.IsNullOrWhiteSpace(args)) {
                AnsiConsole.MarkupLine($"[bold cyan]Current Session Status:[/]");
                AnsiConsole.MarkupLine($"  Provider: [bold]{Markup.Escape(AppState.ActiveProvider)}[/]");
                AnsiConsole.MarkupLine($"  Active Model: [bold]{Markup.Escape(AppState.ActiveModel)}[/]");
                AnsiConsole.WriteLine();

                var modelsMap = new Dictionary<string, (string Provider, string ModelId)>
                {
                    { "Gemini 3.5 Flash (Medium)", ("antigravity-cli", "Gemini 3.5 Flash (Medium)") },
                    { "Gemini 3.5 Flash (High)", ("antigravity-cli", "Gemini 3.5 Flash (High)") },
                    { "Gemini 3.5 Flash (Low)", ("antigravity-cli", "Gemini 3.5 Flash (Low)") },
                    { "Gemini 3.1 Pro (Low)", ("antigravity-cli", "Gemini 3.1 Pro (Low)") },
                    { "Gemini 3.1 Pro (High)", ("antigravity-cli", "Gemini 3.1 Pro (High)") },
                    { "Claude Sonnet 4.6 (Thinking)", ("antigravity-cli", "Claude Sonnet 4.6 (Thinking)") },
                    { "Claude Opus 4.6 (Thinking)", ("antigravity-cli", "Claude Opus 4.6 (Thinking)") },
                    { "GPT-OSS 120B (Medium)", ("antigravity-cli", "GPT-OSS 120B (Medium)") }
                };

                try {
                    string? ollamaUri = AuthManager.GetApiKey("ollama");
                    if (!string.IsNullOrEmpty(ollamaUri)) {
                        var ollama = sp.GetRequiredService<OllamaProvider>();
                        var ollamaModels = await ollama.ListModelsAsync();
                        foreach (var m in ollamaModels) modelsMap[$"[green][Ollama][/] {Markup.Escape(m)}"] = ("ollama", m);
                    }
                } catch { }

                try {
                    string? lmstudioUri = AuthManager.GetApiKey("lmstudio") ?? "http://localhost:1234";
                    if (!string.IsNullOrEmpty(lmstudioUri)) {
                        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                        var client = clientFactory.CreateClient("lmstudio");
                        client.Timeout = TimeSpan.FromSeconds(2);
                        string endpoint = lmstudioUri;
                        string? apiKey = null;
                        int spaceIdx = endpoint.IndexOf(' ');
                        if (spaceIdx > 0) {
                            apiKey = endpoint.Substring(spaceIdx + 1).Trim();
                            endpoint = endpoint.Substring(0, spaceIdx).Trim();
                        }
                        if (!string.IsNullOrEmpty(apiKey)) {
                            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                        }
                        if (!endpoint.Contains("/v1")) endpoint = endpoint.TrimEnd('/') + "/v1";
                        var response = await client.GetAsync($"{endpoint}/models");
                        if (response.IsSuccessStatusCode) {
                            var jsonStr = await response.Content.ReadAsStringAsync();
                            var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                            var data = doc.RootElement.GetProperty("data");
                            foreach (var item in data.EnumerateArray()) {
                                if (item.TryGetProperty("id", out var idProp)) {
                                    string m = idProp.GetString() ?? "";
                                    modelsMap[$"[blue][LMStudio][/] {Markup.Escape(m)}"] = ("lmstudio", m);
                                }
                            }
                        }
                    }
                } catch { }

                modelsMap["[red](Cancel)[/]"] = ("", "");

                var prompt = new SelectionPrompt<string>()
                    .Title("[white]Switch Model[/]")
                    .PageSize(15)
                    .AddChoices(modelsMap.Keys)
                    .UseConverter(label => 
                    {
                        if (modelsMap.TryGetValue(label, out var info))
                        {
                            if (!string.IsNullOrEmpty(info.ModelId) && info.ModelId.Equals(AppState.ActiveModel, StringComparison.OrdinalIgnoreCase))
                            {
                                return $"{label}   [grey](current)[/]";
                            }
                        }
                        return label;
                    });

                var selectedLabel = AnsiConsole.Prompt(prompt);
                var selectedInfo = modelsMap[selectedLabel];

                if (string.IsNullOrEmpty(selectedInfo.Provider)) {
                    return "[grey]Model switch cancelled.[/]";
                }

                AppState.ActiveModel = selectedInfo.ModelId;
                AppState.ActiveProvider = selectedInfo.Provider;
                return $"Model changed to: [bold green]{Markup.Escape(selectedInfo.ModelId)}[/] (Provider: {selectedInfo.Provider})";
            }

            string newModel = args.Trim();
            string detectedProvider = AppState.ActiveProvider;

            if (AppState.ActiveProvider == "gemini-cli")
            {
                detectedProvider = "gemini-cli";
            }
            else
            {
                if (newModel.StartsWith("claude")) detectedProvider = "claude";
                else if (newModel.StartsWith("gemini")) detectedProvider = "gemini";
                else {
                    try {
                        var ollama = sp.GetRequiredService<OllamaProvider>();
                        var ollamaModels = await ollama.ListModelsAsync();
                        if (ollamaModels.Any(m => m.Equals(newModel, StringComparison.OrdinalIgnoreCase))) detectedProvider = "ollama";
                    } catch { }

                    if (detectedProvider != "ollama") {
                        try {
                            string? lmstudioUri = AuthManager.GetApiKey("lmstudio") ?? "http://localhost:1234";
                            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
                            var client = clientFactory.CreateClient("lmstudio");
                            client.Timeout = TimeSpan.FromSeconds(2);
                            string endpoint = lmstudioUri;
                            string? apiKey = null;
                            int spaceIdx = endpoint.IndexOf(' ');
                            if (spaceIdx > 0) {
                                apiKey = endpoint.Substring(spaceIdx + 1).Trim();
                                endpoint = endpoint.Substring(0, spaceIdx).Trim();
                            }
                            if (!string.IsNullOrEmpty(apiKey)) {
                                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                            }
                            if (!endpoint.Contains("/v1")) endpoint = endpoint.TrimEnd('/') + "/v1";
                            var response = await client.GetAsync($"{endpoint}/models");
                            if (response.IsSuccessStatusCode) {
                                var jsonStr = await response.Content.ReadAsStringAsync();
                                var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                                var data = doc.RootElement.GetProperty("data");
                                foreach (var item in data.EnumerateArray()) {
                                    if (item.TryGetProperty("id", out var idProp) && idProp.GetString()?.Equals(newModel, StringComparison.OrdinalIgnoreCase) == true) {
                                        detectedProvider = "lmstudio";
                                        break;
                                    }
                                }
                            }
                        } catch { }
                    }
                }
            }

            AppState.ActiveModel = newModel;
            AppState.ActiveProvider = detectedProvider;
            AppState.IsProviderExplicitlySet = true;
            return $"[green]Model changed to:[/] [bold]{Markup.Escape(newModel)}[/] (Provider switched to: [bold]{Markup.Escape(detectedProvider)}[/])";
        }

        public static Task<string> HandleReset(string args, IServiceProvider sp)
        {
            return Task.FromResult("[yellow]Session reset command issued. Provider history will be cleared on next turn.[/]");
        }
    }
}
