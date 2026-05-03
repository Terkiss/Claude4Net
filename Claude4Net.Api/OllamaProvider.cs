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
    /// 로컬에서 실행되는 Ollama 인스턴스를 통해 LLM 기능을 제공하는 프로바이더입니다.
    /// 로컬 호스트(http://localhost:11434) 통신 및 도구 호출을 지원합니다.
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// OllamaProvider의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="httpClient">HTTP 통신용 클라이언트</param>
        /// <param name="toolRegistry">도구 정보를 관리하는 레지스트리</param>
        public OllamaProvider(HttpClient httpClient, IToolRegistry toolRegistry) 
        { 
            _httpClient = httpClient;
            _toolRegistry = toolRegistry; 
        }

        /// <summary>
        /// 프로바이더의 고유 이름입니다.
        /// </summary>
        public string Name => "ollama";

        /// <summary>
        /// 대화 히스토리에 메시지를 추가합니다. 도구 실행 결과를 Ollama 규격에 맞춰 변환합니다.
        /// </summary>
        /// <param name="message">추가할 메시지 객체</param>
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
                            // Ollama는 도구 결과를 role="tool"과 tool_call_id를 통해 매칭해야 함
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
        /// 현재 대화 히스토리를 반환합니다.
        /// </summary>
        /// <returns>메시지 히스토리 리스트</returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// Ollama 서버에서 사용 가능한 모델 목록을 조회합니다.
        /// </summary>
        /// <returns>모델 이름 리스트</returns>
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
        /// Ollama API를 통해 쿼리를 수행하고 결과를 스트리밍합니다.
        /// </summary>
        /// <param name="prompt">사용자 입력 쿼리</param>
        /// <param name="model">모델명 (예: llama3.1)</param>
        /// <param name="ct">작업 취소 토큰</param>
        /// <returns>스트리밍 이벤트 열거자</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";

            _messageHistory.Add(new { role = "user", content = prompt });

            // 도구 목록 구성
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

            // 응답 파싱 및 스트리밍
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
