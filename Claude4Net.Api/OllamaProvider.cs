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
    /// 로컬?�서 ?�행?�는 Ollama ?�스?�스�??�해 LLM 기능???�공?�는 ?�로바이?�입?�다.
    /// 로컬 ?�스??http://localhost:11434) ?�신 �??�구 ?�출??지?�합?�다.
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// OllamaProvider?????�스?�스�?초기?�합?�다.
        /// </summary>
        /// <param name="httpClient">HTTP ?�신???�라?�언??/param>
        /// <param name="toolRegistry">?�구 ?�보�?관리하???��??�트�?/param>
        public OllamaProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?�로바이?�의 고유 ?�름?�니??
        /// </summary>
        public string Name => "ollama";

        /// <summary>
        /// ?�당 ?�공?�용 ?�큰 카운?��? 가?�옵?�다.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// Ollama의 기본 컨텍스트 윈도우 크기입니다. (256k)
        /// </summary>
        public const int DefaultContextLimit = 262144;

        /// <summary>
        /// 환경 변수 및 설정을 고려하여 유효한 컨텍스트 제한을 계산합니다.
        /// </summary>
        public static int GetEffectiveContextLimit()
        {
            var envVal = Environment.GetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT");
            if (int.TryParse(envVal, out int limit) && limit > 0)
            {
                return Math.Clamp(limit, 8192, 1048576); // 최소 8k, 최대 1M 가드
            }
            return DefaultContextLimit;
        }

        /// <summary>
        /// ?당 ?공?의 ?재 모델 컨텍?트 ?한??가?옵?다. (기본 256k, 환경변수 오버라이드 가능)
        /// </summary>
        public int ContextLimit => GetEffectiveContextLimit();


        /// <summary>
        /// ?�???�스?�리??메시지�?추�??�니?? ?�구 ?�행 결과�?Ollama 규격??맞춰 변?�합?�다.
        /// </summary>
        /// <param name="message">추�???메시지 객체</param>
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
                            var toolUseId = item.GetProperty("tool_use_id").GetString();
                            var contentElement = item.GetProperty("content");

                            // 구조화된 데이터(객체/배열)인 경우 GetRawText()를 사용하여 JSON 문자열로 보존
                            string finalContent = contentElement.ValueKind == JsonValueKind.String
                                ? contentElement.GetString() ?? ""
                                : contentElement.GetRawText();

                            _messageHistory.Add(new
                            {
                                role = "tool",
                                tool_call_id = toolUseId,
                                content = finalContent
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
        /// ?�재 ?�???�스?�리�?반환?�니??
        /// </summary>
        /// <returns>메시지 ?�스?�리 리스??/returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// ?�???�스?�리�??�로??목록?�로 ?�체합?�다.
        /// </summary>
        /// <param name="history">?�체할 메시지 목록</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null) _messageHistory.AddRange(history);
        }

        /// <summary>
        /// Ollama ?�버?�서 ?�용 가?�한 모델 목록??조회?�니??
        /// </summary>
        /// <returns>모델 ?�름 리스??/returns>
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
        /// Ollama API�??�해 쿼리�??�행?�고 결과�??�트리밍?�니??
        /// </summary>
        /// <param name="prompt">?�용???�력 쿼리</param>
        /// <param name="model">모델�?(?? llama3.1)</param>
        /// <param name="ct">?�업 취소 ?�큰</param>
        /// <returns>?�트리밍 ?�벤???�거??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";

            if (!string.IsNullOrEmpty(prompt))
            {
                _messageHistory.Add(new { role = "user", content = prompt });
            }

            // ?구 목록 구성

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

            var payload = new
            {
                model = actualModel,
                messages = finalMessages,
                tools = ollamaTools.Any() ? ollamaTools : null,
                stream = true,
                options = new { num_ctx = ContextLimit }
            };
            var request = new HttpRequestMessage(HttpMethod.Post, $"{uri}/api/chat");
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            var finalRes = new LLMResponse();
            var assistantToolCalls = new List<object>();
            bool toolCalled = false;

            // ?�답 ?�싱 �??�트리밍
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
