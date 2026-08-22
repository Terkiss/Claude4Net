using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// Zhipu AI (智谱AI) GLM 계열 모델과 통신하는 LLM 프로바이더입니다.
    /// GLM OpenAI 호환 API(/v1/chat/completions)를 통해 스트리밍 응답, 도구 호출,
    /// 임베딩 생성을 지원합니다.
    /// </summary>
    public class GlmProvider : ILLMProvider, IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly List<object> _messageHistory = new();

        /// <summary>GLM OpenAI 호환 API 베이스 URL</summary>
        public const string DefaultEndpoint = "https://open.bigmodel.cn/api/paas/v4";

        /// <summary>빠른 응답용 기본 소형 모델 (무료)</summary>
        public const string DefaultSmallModel = "glm-4-flash";

        /// <summary>복잡한 작업용 기본 대형 모델</summary>
        public const string DefaultLargeModel = "glm-4-plus";

        /// <summary>GLM 기본 컨텍스트 윈도우 (토큰 수)</summary>
        public const int DefaultContextWindowSize = 128_000;

        /// <summary>GLM 임베딩 기본 모델</summary>
        public const string DefaultEmbeddingModel = "embedding-3";

        /// <summary>
        /// GLM 프로바이더 인스턴스를 생성합니다.
        /// </summary>
        /// <param name="httpClient">HTTP 클라이언트</param>
        /// <param name="toolRegistry">도구 레지스트리</param>
        public GlmProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        }

        /// <summary>프로바이더 고유 식별자</summary>
        public string Name => "glm";

        public string ProviderId => "glm";

        public string ModelId => DefaultEmbeddingModel;

        /// <summary>토큰 카운터</summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>컨텍스트 윈도우 크기</summary>
        public int ContextLimit => ResolveGlmContextLimit(AppState.ActiveModel);

        public static int ResolveGlmContextLimit(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return DefaultContextWindowSize;
            if (model.Contains("long", StringComparison.OrdinalIgnoreCase)) return 1_000_000;
            return DefaultContextWindowSize;
        }

        // ──────────────────────────────────────────────
        // 메시지 히스토리 관리
        // ──────────────────────────────────────────────

        /// <summary>
        /// 대화 히스토리에 메시지를 추가합니다.
        /// Anthropic 형식의 tool_result 메시지를 OpenAI 호환 tool 응답으로 변환합니다.
        /// </summary>
        public void AddMessage(object message)
        {
            if (message == null) return;

            JsonElement root = default;
            bool isJsonElement = false;

            if (message is JsonElement je)
            {
                root = je;
                isJsonElement = true;
            }
            else
            {
                try
                {
                    using var doc = JsonDocument.Parse(JsonSerializer.Serialize(message));
                    root = doc.RootElement.Clone();
                    isJsonElement = true;
                }
                catch { }
            }

            if (isJsonElement)
            {
                // Anthropic tool_result → OpenAI tool 응답 변환
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

        /// <summary>현재 대화 히스토리를 반환합니다.</summary>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>대화 히스토리를 교체합니다.</summary>
        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null)
            {
                _messageHistory.AddRange(history);
            }
        }

        // ──────────────────────────────────────────────
        // 인증 및 엔드포인트 헬퍼
        // ──────────────────────────────────────────────

        /// <summary>
        /// api_key.json 또는 환경 변수에서 API 키를 가져옵니다.
        /// </summary>
        private static string? ResolveApiKey()
        {
            // 1. api_key.json (AuthManager)
            string? key = AuthManager.GetApiKey("glm");
            if (!string.IsNullOrEmpty(key)) return key;

            // 2. 환경 변수 폴백
            key = Environment.GetEnvironmentVariable("ZHIPUAI_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            return Environment.GetEnvironmentVariable("GLM_API_KEY");
        }

        /// <summary>
        /// 채팅 완성 엔드포인트 URL을 조합합니다.
        /// </summary>
        private static string ResolveChatEndpoint(string? overrideEndpoint = null)
        {
            string baseEndpoint = !string.IsNullOrWhiteSpace(overrideEndpoint)
                ? overrideEndpoint!
                : DefaultEndpoint;

            string baseAddr = baseEndpoint.TrimEnd('/');
            if (baseAddr.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return baseAddr;
            if (baseAddr.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return baseAddr + "/chat/completions";
            if (baseAddr.EndsWith("/v4", StringComparison.OrdinalIgnoreCase))
                return baseAddr + "/chat/completions";
            return baseAddr + "/chat/completions";
        }

        // ──────────────────────────────────────────────
        // 모델 목록 조회
        // ──────────────────────────────────────────────

        /// <summary>
        /// GLM API에서 사용 가능한 모델 목록을 조회합니다.
        /// 조회 실패 시 기본 모델 목록을 반환합니다.
        /// </summary>
        public async Task<List<string>> ListModelsAsync()
        {
            string? apiKey = ResolveApiKey();
            string endpoint = DefaultEndpoint.TrimEnd('/') + "/models";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();

                string jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonStr);

                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    var models = new List<string>();
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var id))
                            models.Add(id.GetString()!);
                    }
                    return models;
                }
            }
            catch { }

            // 폴백: GLM 공식 모델 목록
            return new List<string>
            {
                "glm-4-plus",
                "glm-4-0520",
                "glm-4",
                "glm-4-air",
                "glm-4-flash",
                "glm-4-flashx",
                "glm-4-long",
                DefaultEmbeddingModel
            };
        }

        // ──────────────────────────────────────────────
        // 스트리밍 채팅 (핵심)
        // ──────────────────────────────────────────────

        /// <summary>
        /// GLM API에 쿼리를 전송하고 스트리밍 응답을 반환합니다.
        /// 시스템 프롬프트 주입, 도구 호출(function calling)을 지원합니다.
        /// </summary>
        /// <param name="prompt">사용자 입력</param>
        /// <param name="model">사용할 모델 (기본값: AppState.ActiveModel 또는 glm-4-plus)</param>
        /// <param name="ct">취소 토큰</param>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
            string prompt,
            string? model = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            if (string.IsNullOrEmpty(actualModel)) actualModel = DefaultLargeModel;

            string? apiKey = ResolveApiKey();
            string endpoint = ResolveChatEndpoint();

            if (!string.IsNullOrEmpty(prompt))
            {
                _messageHistory.Add(new { role = "user", content = prompt });
            }

            // 시스템 프롬프트 구성
            var systemMsg = new { role = "system", content = new SystemPromptBuilder().Build("glm") };
            var finalMessages = new List<object> { systemMsg };
            finalMessages.AddRange(_messageHistory);

            // 도구 정의 (OpenAI function calling 형식)
            var tools = _toolRegistry.GetTools();
            var glmTools = new List<object>();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    object parameters = t.InputSchema ?? (object)new { type = "object", properties = new { }, required = new string[] { } };
                    glmTools.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = parameters } });
                }
            }

            var payload = new
            {
                model = actualModel,
                messages = finalMessages,
                tools = glmTools.Any() ? glmTools : null,
                stream = true
            };

            var jsonOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var finalRes = new LLMResponse();
            var toolCallsBuilder = new Dictionary<int, (string id, string name, System.Text.StringBuilder args)>();
            bool toolCalled = false;

            // SSE 스트림 파싱
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string dataLine = line.Trim();
                if (!dataLine.StartsWith("data: ")) continue;

                string jsonStr = dataLine.Substring(6).Trim();
                if (jsonStr.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                JsonElement chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                }
                catch
                {
                    continue;
                }

                if (!chunk.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                    continue;

                var choice = choices[0];
                if (!choice.TryGetProperty("delta", out var delta))
                    continue;

                // 텍스트 델타 처리
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    string text = content.GetString() ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        finalRes.Text += text;
                        yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                    }
                }

                // 도구 호출 누적 처리
                if (delta.TryGetProperty("tool_calls", out var toolCalls))
                {
                    toolCalled = true;
                    foreach (var tc in toolCalls.EnumerateArray())
                    {
                        int index = tc.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                        if (!toolCallsBuilder.ContainsKey(index))
                        {
                            toolCallsBuilder[index] = (string.Empty, string.Empty, new System.Text.StringBuilder());
                        }

                        var current = toolCallsBuilder[index];

                        if (tc.TryGetProperty("id", out var idProp))
                            current.id = idProp.GetString() ?? current.id;

                        if (tc.TryGetProperty("function", out var func))
                        {
                            if (func.TryGetProperty("name", out var nameProp))
                                current.name = nameProp.GetString() ?? current.name;

                            if (func.TryGetProperty("arguments", out var argsProp))
                                current.args.Append(argsProp.GetString());
                        }

                        toolCallsBuilder[index] = current;
                    }
                }
            }

            // 누적된 도구 호출을 이벤트로 발행
            var assistantToolCalls = new List<object>();
            foreach (var kvp in toolCallsBuilder)
            {
                var callInfo = kvp.Value;
                var call = new ToolUseRequest
                {
                    Id = string.IsNullOrEmpty(callInfo.id) ? Guid.NewGuid().ToString() : callInfo.id,
                    Name = callInfo.name
                };

                string argsStr = callInfo.args.ToString();
                if (!string.IsNullOrWhiteSpace(argsStr))
                {
                    try { call.Input = JsonSerializer.Deserialize<object>(argsStr)!; }
                    catch { call.Input = argsStr; }
                }

                finalRes.ToolCalls.Add(call);
                assistantToolCalls.Add(new
                {
                    id = call.Id,
                    type = "function",
                    function = new { name = call.Name, arguments = argsStr }
                });
                yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = call };
            }

            // 도구만 호출되고 텍스트가 없으면 안내 메시지
            if (string.IsNullOrEmpty(finalRes.Text) && toolCalled)
            {
                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "[italic grey](Executing local tools...)[/]\n" };
            }

            // 어시스턴트 응답을 히스토리에 추가
            _messageHistory.Add(new
            {
                role = "assistant",
                content = string.IsNullOrEmpty(finalRes.Text) ? null : finalRes.Text,
                tool_calls = assistantToolCalls.Any() ? assistantToolCalls : null
            });

            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalRes };
        }

        // ──────────────────────────────────────────────
        // 임베딩 (IEmbeddingProvider)
        // ──────────────────────────────────────────────

        /// <summary>
        /// 단일 텍스트에 대한 임베딩 벡터를 반환합니다.
        /// </summary>
        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            var res = await GetEmbeddingsAsync(new[] { text }, ct);
            return res.FirstOrDefault() ?? Array.Empty<float>();
        }

        /// <summary>
        /// 여러 텍스트에 대한 임베딩 벡터 목록을 반환합니다.
        /// GLM 임베딩 API(/v4/embeddings)를 사용합니다.
        /// </summary>
        public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            if (texts == null || !texts.Any()) return new List<float[]>();

            string? apiKey = ResolveApiKey();
            string endpoint = DefaultEndpoint.TrimEnd('/') + "/embeddings";

            var payload = new
            {
                model = DefaultEmbeddingModel,
                input = texts.ToArray()
            };

            var jsonOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), jsonOptions)
            };

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            string jsonStr = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonStr);

            var results = new List<float[]>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("embedding", out var embArray) && embArray.ValueKind == JsonValueKind.Array)
                    {
                        var vec = new List<float>();
                        foreach (var val in embArray.EnumerateArray())
                        {
                            vec.Add(val.GetSingle());
                        }
                        results.Add(vec.ToArray());
                    }
                }
            }
            return results;
        }
    }
}
