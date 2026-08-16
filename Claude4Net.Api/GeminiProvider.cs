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
    /// <summary>
    /// LLM provider that communicates with the Google Gemini API for generative AI and tool use capabilities.
    /// Converts Anthropic-format messages to Gemini specification for cross-provider compatibility.
    /// </summary>
    public class GeminiProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;
        private readonly Dictionary<string, string> _toolCallIdToNameMap = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="GeminiProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client for API requests.</param>
        /// <param name="toolRegistry">The registry managing available tool definitions.</param>
        public GeminiProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// Gets the unique provider name identifier.
        /// </summary>
        public string Name => "gemini";

        /// <summary>
        /// Gets the token counter for this provider.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// Gets the maximum context window size dynamically resolved from the active model.
        /// Gemini Pro (1.5 / 2.0 / 2.5): 2,000,000 (2M) tokens
        /// Gemini Flash (1.5 / 2.0 / 2.5 / 8B): 1,000,000 (1M) tokens
        /// Gemini 1.0 Pro: 32,768 tokens
        /// </summary>
        public int ContextLimit => ResolveGeminiContextLimit(AppState.ActiveModel);

        public static int ResolveGeminiContextLimit(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return 1_000_000;
            string lower = model.ToLowerInvariant();
            if (lower.Contains("1.0") || lower == "gemini-pro") return 32_768;
            if (lower.Contains("pro")) return 2_000_000;
            if (lower.Contains("flash")) return 1_000_000;
            return 1_000_000;
        }

        /// <summary>
        /// Appends a message to the conversation history, converting from Anthropic format to Gemini format.
        /// Handles tool_result messages by wrapping them as functionResponse parts for Gemini compatibility.
        /// </summary>
        /// <param name="message">The message object to add (supports Anthropic-format messages).</param>
        public void AddMessage(object message)
        {
            if (message == null) return;

            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Attempt to convert Anthropic message format to Gemini format
                if (root.TryGetProperty("role", out var roleProp))
                {
                    string role = roleProp.GetString() ?? "user";

                    if (root.TryGetProperty("content", out var contentProp))
                    {
                        var parts = new List<object>();
                        bool hasFunctionResponse = false;

                        if (contentProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentProp.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_result")
                                {
                                    hasFunctionResponse = true;
                                    // Convert tool execution result to Gemini functionResponse format
                                    string toolUseId = item.GetProperty("tool_use_id").GetString() ?? "unknown";
                                    string functionName = _toolCallIdToNameMap.TryGetValue(toolUseId, out var name) ? name : toolUseId;

                                    var contentElement = item.GetProperty("content");
                                    object responseObj;

                                    if (contentElement.ValueKind == JsonValueKind.String)
                                    {
                                        responseObj = new { content = contentElement.GetString() ?? "" };
                                    }
                                    else if (contentElement.ValueKind == JsonValueKind.Null)
                                    {
                                        responseObj = new { content = "null" };
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (contentElement.ValueKind == JsonValueKind.Object)
                                            {
                                                responseObj = JsonSerializer.Deserialize<object>(contentElement.GetRawText()) ?? new { };
                                            }
                                            else
                                            {
                                                responseObj = new { content = contentElement.GetRawText() };
                                            }
                                        }
                                        catch
                                        {
                                            responseObj = new { content = contentElement.ToString() };
                                        }
                                    }

                                    parts.Add(new
                                    {
                                        functionResponse = new
                                        {
                                            name = functionName,
                                            response = responseObj
                                        }
                                    });
                                }
                                else if (item.TryGetProperty("type", out var tProp) && tProp.GetString() == "text")
                                {
                                    parts.Add(new { text = item.GetProperty("text").GetString() ?? "" });
                                }
                                else
                                {
                                    parts.Add(new { text = item.ToString() });
                                }
                            }
                        }
                        else
                        {
                            parts.Add(new { text = contentProp.GetString() ?? "" });
                        }

                        string geminiRole = hasFunctionResponse ? "function" : role;
                        _conversationHistory.Add(new { role = geminiRole, parts = parts });
                        return;
                    }
                }
            }
            catch
            {
                try
                {
                    var fallbackJson = JsonSerializer.Serialize(message);
                    _conversationHistory.Add(new { role = "user", parts = new[] { new { text = fallbackJson } } });
                    return;
                }
                catch { }
            }

            _conversationHistory.Add(new { role = "user", parts = new[] { new { text = message?.ToString() ?? "null" } } });
        }

        /// <summary>
        /// Returns the current conversation history as a read-only list.
        /// </summary>
        /// <returns>A read-only list of message objects.</returns>
        public IReadOnlyList<object> GetHistory() => _conversationHistory.AsReadOnly();

        /// <summary>
        /// Replaces the entire conversation history with a new set of messages.
        /// </summary>
        /// <param name="history">The new message collection to use as history.</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _conversationHistory.Clear();
            if (history != null) _conversationHistory.AddRange(history);
        }

        /// <summary>
        /// Sends a query to the Gemini API and streams the response asynchronously.
        /// Includes system prompt construction and tool (function) declaration support.
        /// Handles thinking model configuration and safety filter responses.
        /// </summary>
        /// <param name="prompt">The user input query to send.</param>
        /// <param name="model">Optional model name (e.g., gemini-1.5-pro).</param>
        /// <param name="ct">Cancellation token to abort the streaming operation.</param>
        /// <returns>An asynchronous stream of LLM stream events.</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? apiKey = AuthManager.GetGeminiApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API key is missing.");

            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", parts = new[] { new { text = prompt } } });
            }

            // Build function declarations from the tool registry for Gemini's tool format
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

            // Configure generation settings including thinking model support
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

            // Parse SSE stream events from the Gemini API response
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

                    // Handle safety filter blocks
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
                            // Capture the entire part to preserve metadata like thought_signature
                            assistantParts.Add(part.Clone());

                            if (part.TryGetProperty("text", out var textProp))
                            {
                                string text = textProp.GetString() ?? "";
                                fullText.Append(text);
                                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                            }
                            else if (part.TryGetProperty("functionCall", out var funcCall))
                            {
                                // Process function call responses from the model
                                string callName = funcCall.GetProperty("name").GetString()!;
                                // Gemini requires that the response name matches the call name EXACTLY.
                                // We use a synthetic ID for internal tracking in ToolUseRequest,
                                // but we MUST map it back to the original name in AddMessage.
                                string callId = $"{callName}_{toolCallIndex++}";
                                var call = new ToolUseRequest { Id = callId, Name = callName, Input = funcCall.GetProperty("args").Clone() };

                                _toolCallIdToNameMap[callId] = callName;

                                toolCalls.Add(call);
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
