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
    /// Google Gemini APIë¥??œìš©?˜ì—¬ ?€?”í˜• AI ë°??„êµ¬ ?¸ì¶œ ê¸°ëŠ¥???œê³µ?˜ëŠ” ?„ë¡œë°”ì´?”ì…?ˆë‹¤.
    /// Anthropic ?•ì‹??ë©”ì‹œì§€ë¥?Gemini ê·œê²©?¼ë¡œ ë³€?˜í•˜???í˜¸ ?¸í™˜?±ì„ ? ì??©ë‹ˆ??
    /// </summary>
    public class GeminiProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;
        private readonly Dictionary<string, string> _toolCallIdToNameMap = new();

        /// <summary>
        /// GeminiProvider?????¸ìŠ¤?´ìŠ¤ë¥?ì´ˆê¸°?”í•©?ˆë‹¤.
        /// </summary>
        /// <param name="httpClient">HTTP ?”ì²­???„í•œ ?´ë¼?´ì–¸??/param>
        /// <param name="toolRegistry">?„êµ¬ ?±ë¡ ?•ë³´ë¥?ê´€ë¦¬í•˜???ˆì??¤íŠ¸ë¦?/param>
        public GeminiProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?„ë¡œë°”ì´?”ì˜ ê³ ìœ  ?´ë¦„?…ë‹ˆ??
        /// </summary>
        public string Name => "gemini";

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ìš© ? í° ì¹´ìš´?°ë? ê°€?¸ì˜µ?ˆë‹¤.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ì˜ ?„ì¬ ëª¨ë¸ ì»¨í…?¤íŠ¸ ?œí•œ??ê°€?¸ì˜µ?ˆë‹¤. (Gemini 1.5 ê¸°ì? 1M)
        /// </summary>
        public int ContextLimit => 1000000;

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬??ë©”ì‹œì§€ë¥?ì¶”ê??˜ë©°, Anthropic ?•ì‹??Gemini ?•ì‹?¼ë¡œ ë³€?˜í•©?ˆë‹¤.
        /// </summary>
        /// <param name="message">ì¶”ê???ë©”ì‹œì§€ ê°ì²´ (Anthropic ê·œê²© ? í˜¸)</param>
        public void AddMessage(object message)
        {
            if (message == null) return;

            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Anthropic ë©”ì‹œì§€ë¥?Gemini ?•ì‹?¼ë¡œ ë³€???œë„
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
                                    // ?„êµ¬ ?¤í–‰ ê²°ê³¼ ë³€??
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
                // ë³€???¤íŒ¨ ???ë³¸ ë©”ì‹œì§€ ì¶”ê?
            }

            _conversationHistory.Add(message);
        }

        /// <summary>
        /// ?„ì¬ ?€???ˆìŠ¤? ë¦¬ë¥?ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <returns>ë©”ì‹œì§€ ê°ì²´ ë¦¬ìŠ¤??/returns>
        public IReadOnlyList<object> GetHistory() => _conversationHistory.AsReadOnly();

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬ë¥??ˆë¡œ??ëª©ë¡?¼ë¡œ ?€ì²´í•©?ˆë‹¤.
        /// </summary>
        /// <param name="history">?€ì²´í•  ë©”ì‹œì§€ ëª©ë¡</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _conversationHistory.Clear();
            if (history != null) _conversationHistory.AddRange(history);
        }

        /// <summary>
        /// Gemini APIë¥??¸ì¶œ?˜ì—¬ ê²°ê³¼ë¥??¤íŠ¸ë¦¬ë°?©ë‹ˆ?? ?œìŠ¤???„ë¡¬?„íŠ¸ ë°??„êµ¬ ?•ì˜ê°€ ?¬í•¨?©ë‹ˆ??
        /// </summary>
        /// <param name="prompt">?¬ìš©???…ë ¥ ì¿¼ë¦¬</param>
        /// <param name="model">ëª¨ë¸ëª?(?? gemini-1.5-pro)</param>
        /// <param name="ct">?‘ì—… ì·¨ì†Œ ? í°</param>
        /// <returns>?¤íŠ¸ë¦¬ë° ?´ë²¤???´ê±°??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? apiKey = AuthManager.GetGeminiApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API key is missing.");

            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", parts = new[] { new { text = prompt } } });
            }

            // ?„êµ¬ ? ì–¸ (Function Declarations) êµ¬ì„±
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

            // ?ì„± ?¤ì • (Thinking Config ì§€???¬í•¨)
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

            // SSE ?¤íŠ¸ë¦??Œì‹±
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

                    // ?ˆì „ ?„í„°ë§?ì²˜ë¦¬
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
                                // ?„êµ¬ ?¸ì¶œ ì²˜ë¦¬
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
