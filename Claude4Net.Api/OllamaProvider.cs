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
    /// <summary>
    /// ë¡œì»¬?ì„œ ?¤í–‰?˜ëŠ” Ollama ?¸ìŠ¤?´ìŠ¤ë¥??µí•´ LLM ê¸°ëŠ¥???œê³µ?˜ëŠ” ?„ë¡œë°”ì´?”ì…?ˆë‹¤.
    /// ë¡œì»¬ ?¸ìŠ¤??http://localhost:11434) ?µì‹  ë°??„êµ¬ ?¸ì¶œ??ì§€?í•©?ˆë‹¤.
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// OllamaProvider?????¸ìŠ¤?´ìŠ¤ë¥?ì´ˆê¸°?”í•©?ˆë‹¤.
        /// </summary>
        /// <param name="httpClient">HTTP ?µì‹ ???´ë¼?´ì–¸??/param>
        /// <param name="toolRegistry">?„êµ¬ ?•ë³´ë¥?ê´€ë¦¬í•˜???ˆì??¤íŠ¸ë¦?/param>
        public OllamaProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?„ë¡œë°”ì´?”ì˜ ê³ ìœ  ?´ë¦„?…ë‹ˆ??
        /// </summary>
        public string Name => "ollama";

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ìš© ? í° ì¹´ìš´?°ë? ê°€?¸ì˜µ?ˆë‹¤.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ì˜ ?„ì¬ ëª¨ë¸ ì»¨í…?¤íŠ¸ ?œí•œ??ê°€?¸ì˜µ?ˆë‹¤. (ë¡œì»¬ ê¸°ë³¸ 8k)
        /// </summary>
        public int ContextLimit => 8192;

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬??ë©”ì‹œì§€ë¥?ì¶”ê??©ë‹ˆ?? ?„êµ¬ ?¤í–‰ ê²°ê³¼ë¥?Ollama ê·œê²©??ë§ì¶° ë³€?˜í•©?ˆë‹¤.
        /// </summary>
        /// <param name="message">ì¶”ê???ë©”ì‹œì§€ ê°ì²´</param>
        public void AddMessage(object message)
        {
            if (message is { } obj)
            {
                var json = JsonSerializer.Serialize(obj);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("role", out var roleProp) && roleProp.GetString() == "user" &&
                    root.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array)
                {
                    bool handled = false;
                    foreach (var item in contentProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_result")
                        {
                            // Ollama???„êµ¬ ê²°ê³¼ë¥?role="tool"ê³?tool_call_idë¥??µí•´ ë§¤ì¹­?´ì•¼ ??
                            _messageHistory.Add(new
                            {
                                role = "tool",
                                tool_call_id = item.GetProperty("tool_use_id").GetString(),
                                content = item.GetProperty("content").GetString() ?? ""
                            });
                            handled = true;
                        }
                    }
                    if (handled) return;
                }
            }
            _messageHistory.Add(message);
        }

        /// <summary>
        /// ?„ì¬ ?€???ˆìŠ¤? ë¦¬ë¥?ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <returns>ë©”ì‹œì§€ ?ˆìŠ¤? ë¦¬ ë¦¬ìŠ¤??/returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬ë¥??ˆë¡œ??ëª©ë¡?¼ë¡œ ?€ì²´í•©?ˆë‹¤.
        /// </summary>
        /// <param name="history">?€ì²´í•  ë©”ì‹œì§€ ëª©ë¡</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null) _messageHistory.AddRange(history);
        }

        /// <summary>
        /// Ollama ?œë²„?ì„œ ?¬ìš© ê°€?¥í•œ ëª¨ë¸ ëª©ë¡??ì¡°íšŒ?©ë‹ˆ??
        /// </summary>
        /// <returns>ëª¨ë¸ ?´ë¦„ ë¦¬ìŠ¤??/returns>
        public async Task<List<string>> ListModelsAsync()
        {
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";
            try
            {
                var response = await _httpClient.GetFromJsonAsync<JsonElement>($"{uri}/api/tags");
                if (response.TryGetProperty("models", out var models))
                {
                    return models.EnumerateArray().Select(m => m.GetProperty("name").GetString()!).ToList();
                }
            }
            catch { }
            return new List<string> { "llama3" };
        }

        /// <summary>
        /// Ollama APIë¥??µí•´ ì¿¼ë¦¬ë¥??˜í–‰?˜ê³  ê²°ê³¼ë¥??¤íŠ¸ë¦¬ë°?©ë‹ˆ??
        /// </summary>
        /// <param name="prompt">?¬ìš©???…ë ¥ ì¿¼ë¦¬</param>
        /// <param name="model">ëª¨ë¸ëª?(?? llama3.1)</param>
        /// <param name="ct">?‘ì—… ì·¨ì†Œ ? í°</param>
        /// <returns>?¤íŠ¸ë¦¬ë° ?´ë²¤???´ê±°??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";

            _messageHistory.Add(new { role = "user", content = prompt });

            // ?„êµ¬ ëª©ë¡ êµ¬ì„±
            var tools = _toolRegistry.GetTools();
            var ollamaTools = new List<object>();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    object parameters = t.InputSchema ?? (object)new { type = "object", properties = new { }, required = new string[] { } };
                    ollamaTools.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = parameters } });
                }
            }

            var systemMsg = new
            {
                role = "system",
                content = new SystemPromptBuilder().Build("ollama")
            };

            var finalMessages = new List<object> { systemMsg };
            finalMessages.AddRange(_messageHistory);

            var payload = new { model = actualModel, messages = finalMessages, tools = ollamaTools.Any() ? ollamaTools : null, stream = true };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{uri}/api/chat");
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            var finalRes = new LLMResponse();
            var assistantToolCalls = new List<object>();
            bool toolCalled = false;

            // ?‘ë‹µ ?Œì‹± ë°??¤íŠ¸ë¦¬ë°
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var chunk = JsonSerializer.Deserialize<JsonElement>(line);
                if (chunk.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        string delta = content.GetString() ?? "";
                        finalRes.Text += delta;
                        if (!string.IsNullOrEmpty(delta)) yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = delta };
                    }

                    if (msg.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            toolCalled = true;
                            var func = tc.GetProperty("function");
                            string callId = tc.GetProperty("id").GetString() ?? Guid.NewGuid().ToString();

                            var call = new ToolUseRequest
                            {
                                Id = callId,
                                Name = func.GetProperty("name").GetString()!
                            };

                            if (func.TryGetProperty("arguments", out var args))
                            {
                                if (args.ValueKind == JsonValueKind.String) call.Input = JsonSerializer.Deserialize<object>(args.GetString()!)!;
                                else call.Input = args;
                            }

                            finalRes.ToolCalls.Add(call);
                            assistantToolCalls.Add(new { id = callId, type = "function", function = func });
                            yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = call };
                        }
                    }
                }
                if (chunk.TryGetProperty("done", out var done) && done.GetBoolean()) break;
            }

            if (string.IsNullOrEmpty(finalRes.Text) && toolCalled) yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "[italic grey](Executing local tools...)[/]\n" };

            _messageHistory.Add(new { role = "assistant", content = finalRes.Text, tool_calls = assistantToolCalls.Any() ? assistantToolCalls : null });
            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalRes };
        }
    }
}
