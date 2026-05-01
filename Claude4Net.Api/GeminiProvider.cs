using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.IO;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Api
{
    public class GeminiProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;
        private readonly Dictionary<string, string> _toolCallIdToNameMap = new();

        public GeminiProvider(HttpClient httpClient, IToolRegistry toolRegistry) 
        { 
            _httpClient = httpClient;
            _toolRegistry = toolRegistry; 
        }

        public string Name => "gemini";

        public void AddMessage(object message)
        {
            if (message == null) return;

            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Anthropic -> Gemini Format Conversion
                if (root.TryGetProperty("role", out var roleProp))
                {
                    string role = roleProp.GetString() ?? "user";

                    // If it has 'content' instead of 'parts', convert it
                    if (root.TryGetProperty("content", out var contentProp))
                    {
                        var parts = new List<object>();

                        if (contentProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentProp.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_result")
                                {
                                    // Gemini Tool Result Format
                                    string toolUseId = item.GetProperty("tool_use_id").GetString() ?? "unknown";
                                    string functionName = _toolCallIdToNameMap.TryGetValue(toolUseId, out var name) ? name : toolUseId;

                                    parts.Add(new
                                    {
                                        functionResponse = new
                                        {
                                            name = functionName,
                                            response = new { content = item.GetProperty("content").GetString() ?? "" }
                                        }
                                    });
                                }
                                else if (item.TryGetProperty("type", out var tProp) && tProp.GetString() == "text")
                                {
                                    parts.Add(new { text = item.GetProperty("text").GetString() ?? "" });
                                }
                                else
                                {
                                    // Fallback for simple strings or unknown types
                                    parts.Add(new { text = item.ToString() });
                                }
                            }
                        }
                        else
                        {
                            // Simple string content
                            parts.Add(new { text = contentProp.GetString() ?? "" });
                        }

                        string geminiRole = (parts.Any(p => json.Contains("functionResponse"))) ? "function" : role;
                        _conversationHistory.Add(new { role = geminiRole, parts = parts });
                        ApplySlidingWindow();
                        return;
                    }
                }
            }
            catch
            {
                // Fallback to raw message if parsing fails
            }

            _conversationHistory.Add(message);
            ApplySlidingWindow();
        }

        private void ApplySlidingWindow()
        {
            const int MAX_HISTORY = 16; // Approx 8 turns
            if (_conversationHistory.Count > MAX_HISTORY)
            {
                int toRemove = _conversationHistory.Count - MAX_HISTORY;
                _conversationHistory.RemoveRange(0, toRemove);
            }
        }

        public IReadOnlyList<object> GetHistory() => _conversationHistory.AsReadOnly();

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? apiKey = AuthManager.GetGeminiApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API key is missing.");

            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", parts = new[] { new { text = prompt } } });
            }

            var tools = _toolRegistry.GetTools();
            var geminiTools = new List<object>();
            if (tools != null && tools.Any())
            {
                var declarations = tools.Select(t => new
                {
                    name = t.Name.Replace("__", "_").Replace("-", "_"),
                    description = (t.Description ?? t.Name) + " (Executes on user's ACTUAL local machine)",
                    parameters = t.InputSchema ?? (object)new { type = "OBJECT", properties = new { }, required = new string[] { } }
                }).ToList();
                geminiTools.Add(new { function_declarations = declarations });
            }

            string modelId = actualModel.Contains("/") ? actualModel.Split('/').Last() : actualModel;
            var url = $"{BASE_URL}/{modelId}:streamGenerateContent?alt=sse&key={apiKey}";

            object? generationCfg;
            if (actualModel.Contains("think", StringComparison.OrdinalIgnoreCase) || actualModel.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
            {
                generationCfg = new { 
                    maxOutputTokens = 8192, 
                    temperature = 0.7,
                    thinkingConfig = new { thinkingLevel = "HIGH", includeThoughts = true }
                };
            }
            else
            {
                generationCfg = new { maxOutputTokens = 8192, temperature = 0.7 };
            }

            var payload = new
            {
                system_instruction = new { parts = new[] { new { text = new SystemPromptBuilder().Build("gemini") } } },
                contents = _conversationHistory,
                tools = geminiTools.Any() ? geminiTools : null,
                generationConfig = generationCfg
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new Exception($"Gemini API Error ({response.StatusCode}): {errorBody}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var fullText = new StringBuilder();
            var toolCalls = new List<ToolUseRequest>();
            var assistantParts = new List<object>();
            int toolCallIndex = 0;

            while (await reader.ReadLineAsync() is { } line)
            {
                if (ct.IsCancellationRequested) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("data: ")) line = line.Substring(6);
                if (line == "[" || line == "," || line == "]") continue;

                JsonElement chunk;
                try { chunk = JsonSerializer.Deserialize<JsonElement>(line); } catch { continue; }

                if (chunk.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];

                    if (candidate.TryGetProperty("finishReason", out var reasonProp))
                    {
                        if (reasonProp.GetString() == "SAFETY")
                        {
                            string safetyMsg = "\n[Gemini Safety Filter] Response blocked.";
                            yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = safetyMsg };
                            fullText.Append(safetyMsg);
                        }
                    }

                    if (candidate.TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
                    {
                        foreach (var part in parts.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var textProp))
                            {
                                string text = textProp.GetString() ?? "";
                                fullText.Append(text);
                                assistantParts.Add(new { text = text });
                                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                            }
                            else if (part.TryGetProperty("functionCall", out var funcCall))
                            {
                                string callName = funcCall.GetProperty("name").GetString()!;
                                string callId = $"{callName}_{toolCallIndex++}";
                                var call = new ToolUseRequest { Id = callId, Name = callName, Input = funcCall.GetProperty("args") };
                                
                                _toolCallIdToNameMap[callId] = callName;
                                
                                toolCalls.Add(call);
                                assistantParts.Add(new { functionCall = new { name = callName, args = call.Input } });
                                yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = call };
                            }
                        }
                    }
                }
            }

            if (assistantParts.Count > 0)
            {
                _conversationHistory.Add(new { role = "model", parts = assistantParts });
            }

            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = new LLMResponse { Text = fullText.ToString(), ToolCalls = toolCalls } };
        }
    }
}
