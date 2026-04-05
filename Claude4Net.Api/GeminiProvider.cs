using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Api
{
    public class GeminiProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        public GeminiProvider(IToolRegistry toolRegistry) { _toolRegistry = toolRegistry; }

        public string Name => "gemini";

        public void AddMessage(object message)
        {
            if (message is { } obj)
            {
                var json = JsonSerializer.Serialize(obj);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // 1. Intercept tool results from AgentLoop (Anthropic format)
                if (root.TryGetProperty("role", out var roleProp) && roleProp.GetString() == "user" &&
                    root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<object>();
                    bool isToolResult = false;

                    foreach (var item in contentProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_result")
                        {
                            isToolResult = true;
                            // Gemini requires functionResponse within parts
                            parts.Add(new
                            {
                                functionResponse = new
                                {
                                    name = item.GetProperty("tool_use_id").GetString() ?? "unknown", // Map back to name if possible, or use ID as fallback
                                    response = new { content = item.GetProperty("content").GetString() ?? "" }
                                }
                            });
                        }
                    }

                    if (isToolResult)
                    {
                        // Gemini tool results MUST have role "function"
                        _conversationHistory.Add(new { role = "function", parts = parts });
                        return;
                    }
                }
            }

            _conversationHistory.Add(message);
        }

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            var tools = _toolRegistry.GetTools();
            var result = await GenerateContentAsync(prompt, tools, actualModel, ct);

            if (!string.IsNullOrEmpty(result.Text)) yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = result.Text };
            foreach (var call in result.ToolCalls) yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = call };
            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = result };
        }

        public async Task<LLMResponse> GenerateContentAsync(string? prompt, IEnumerable<ITool>? tools, string model, CancellationToken ct)
        {
            string? apiKey = AuthManager.GetGeminiApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API key is missing.");

            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", parts = new[] { new { text = prompt } } });
            }

            var geminiTools = new List<object>();
            if (tools != null && tools.Any())
            {
                var declarations = tools.Select(t => new {
                    name = t.Name.Replace("__", "_").Replace("-", "_"),
                    description = (t.Description ?? t.Name) + " (Executes on user's ACTUAL local machine)",
                    parameters = t.Name switch {
                        "BashTool" => (object)new { type = "OBJECT", properties = new { command = new { type = "STRING" } }, required = new[] { "command" } },
                        "FileReadTool" => (object)new { type = "OBJECT", properties = new { file_path = new { type = "STRING" } }, required = new[] { "file_path" } },
                        "LsTool" => (object)new { type = "OBJECT", properties = new { path = new { type = "STRING" } }, required = new[] { "path" } },
                        _ => (object)new { type = "OBJECT", properties = new { }, required = new string[] { } }
                    }
                }).ToList();
                geminiTools.Add(new { function_declarations = declarations });
            }

            var systemInstruction = new
            {
                parts = new[] { new { text = @"
# [Gemini 3.0 Antigravity: Local System Execution Protocol]
당신은 로컬 시스템 에이전트입니다. 도구를 사용하여 사용자의 시스템 관리 요청을 완수하십시오.
" } }
            };

            string modelId = model.Contains("/") ? model.Split('/').Last() : model;
            var url = $"{BASE_URL}/{modelId}:generateContent?key={apiKey}";
            
            var payload = new { 
                system_instruction = systemInstruction,
                contents = _conversationHistory, 
                tools = geminiTools.Any() ? geminiTools : null, 
                generationConfig = new { maxOutputTokens = 8192, temperature = 0.7 } 
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
            if (!response.IsSuccessStatusCode) throw new Exception($"Gemini API Error: {await response.Content.ReadAsStringAsync(ct)}");

            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            var result = new LLMResponse();

            if (data.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];
                if (candidate.TryGetProperty("content", out var content))
                {
                    var assistantParts = new List<object>();
                    if (content.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp))
                            {
                                string text = textProp.GetString() ?? "";
                                result.Text += text;
                                assistantParts.Add(new { text = text });
                            }
                            else if (part.TryGetProperty("functionCall", out var funcCall))
                            {
                                string callName = funcCall.GetProperty("name").GetString()!;
                                var call = new ToolUseRequest { 
                                    Id = callName, // USE NAME AS ID FOR MATCHING
                                    Name = callName, 
                                    Input = funcCall.GetProperty("args") 
                                };
                                result.ToolCalls.Add(call);
                                assistantParts.Add(new { functionCall = new { name = callName, args = call.Input } });
                            }
                        }
                    }
                    // Crucial: Add the assistant's turn correctly to history
                    _conversationHistory.Add(new { role = "model", parts = assistantParts });
                }
            }
            return result;
        }
    }
}
