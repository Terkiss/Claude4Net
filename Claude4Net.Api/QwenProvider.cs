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
    /// Alibaba Cloud (阿里云) Token Plan / Coding Plan 및 DashScope Qwen 계열 고성능 LLM 프로바이더입니다.
    /// 알리바바 토큰 플랜 전용 엔드포인트(https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1)를 기본 지원하며,
    /// Qwen 3.8 Max, Qwen 3.7 Plus/Max, Qwen 3.6 Flash, DeepSeek-V4 Pro/Flash, GLM-5.2 등 최신 플릿의
    /// 스트리밍, Reasoning/Thinking Stream, 도구 호출, 임베딩을 완벽 지원합니다.
    /// </summary>
    public class QwenProvider : ILLMProvider, IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly List<object> _messageHistory = new();

        /// <summary>Alibaba Token Plan 기본 전용 엔드포인트 URL (2026)</summary>
        public const string TokenPlanEndpoint = "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1";

        /// <summary>기본 엔드포인트 (Token Plan Endpoint 매핑)</summary>
        public const string DefaultEndpoint = TokenPlanEndpoint;

        /// <summary>Alibaba DashScope 기본 엔드포인트</summary>
        public const string DashScopeEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";

        /// <summary>Alibaba DashScope 국제 리전(International) 엔드포인트 URL</summary>
        public const string InternationalEndpoint = "https://dashscope-intl.aliyuncs.com/compatible-mode/v1";

        // ──────────────────────────────────────────────
        // 2026 Alibaba Token Plan 플래그십 모델 라인업
        // ──────────────────────────────────────────────

        /// <summary>Qwen 3.8 Max (Text, Reasoning, Visual Understanding / 야간 50% 할인 플랜)</summary>
        public const string ModelQwen38Max = "qwen3.8-max";

        /// <summary>Qwen 3.7 Plus (Text, Reasoning, Visual Understanding)</summary>
        public const string ModelQwen37Plus = "qwen3.7-plus";

        /// <summary>Qwen 3.7 Max (Text, Reasoning)</summary>
        public const string ModelQwen37Max = "qwen3.7-max";

        /// <summary>Qwen 3.6 Flash (고속 저지연, Text, Reasoning, Visual)</summary>
        public const string ModelQwen36Flash = "qwen3.6-flash";

        /// <summary>DeepSeek V4 Pro 0813 (알리바바 토큰 플랜 호스팅, Reasoning)</summary>
        public const string ModelDeepSeekV4Pro0813 = "deepseek-v4-pro-0813";

        /// <summary>DeepSeek V4 Pro (Reasoning)</summary>
        public const string ModelDeepSeekV4Pro = "deepseek-v4-pro";

        /// <summary>DeepSeek V4 Flash 0731 (고속 Reasoning)</summary>
        public const string ModelDeepSeekV4Flash0731 = "deepseek-v4-flash-0731";

        /// <summary>Zhipu GLM 5.2 (알리바바 토큰 플랜 호스팅, Reasoning)</summary>
        public const string ModelGlm52 = "glm-5.2";

        /// <summary>Qwen 2.5 Coder 32B Instruct</summary>
        public const string ModelQwen25Coder32B = "qwen-2.5-coder-32b-instruct";

        /// <summary>Qwen 2.5 Coder 7B Instruct</summary>
        public const string ModelQwen25Coder7B = "qwen-2.5-coder-7b-instruct";

        /// <summary>기본 소형 모델</summary>
        public const string DefaultSmallModel = ModelQwen36Flash;

        /// <summary>기본 대형 플래그십 모델</summary>
        public const string DefaultLargeModel = ModelQwen38Max;

        /// <summary>기본 컨텍스트 윈도우 (131,072 = 128K)</summary>
        public const int DefaultContextWindowSize = 131_072;

        /// <summary>기본 임베딩 모델</summary>
        public const string DefaultEmbeddingModel = "text-embedding-v3";

        /// <summary>
        /// Qwen 프로바이더 인스턴스를 생성합니다.
        /// </summary>
        /// <param name="httpClient">HTTP 클라이언트</param>
        /// <param name="toolRegistry">도구 레지스트리</param>
        public QwenProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        }

        /// <summary>프로바이더 고유 식별자</summary>
        public string Name => "qwen";

        public string ProviderId => "qwen";

        public string ModelId => DefaultEmbeddingModel;

        /// <summary>토큰 카운터</summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>컨텍스트 윈도우 크기</summary>
        public int ContextLimit => ResolveQwenContextLimit(AppState.ActiveModel);

        public static int ResolveQwenContextLimit(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return DefaultContextWindowSize;
            if (model.Contains("1m", StringComparison.OrdinalIgnoreCase) || model.Contains("long", StringComparison.OrdinalIgnoreCase))
                return 1_000_000;
            if (model.Contains("3.8", StringComparison.OrdinalIgnoreCase) || model.Contains("3.7", StringComparison.OrdinalIgnoreCase))
                return 262_144; // 256K context for Qwen 3.8/3.7 series
            return DefaultContextWindowSize;
        }

        // ──────────────────────────────────────────────
        // 메시지 히스토리 관리
        // ──────────────────────────────────────────────

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

        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

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

        private static string? ResolveApiKey()
        {
            string? key = AuthManager.GetApiKey("qwen") ?? AuthManager.GetApiKey("alibaba") ?? AuthManager.GetApiKey("dashscope") ?? AuthManager.GetApiKey("token-plan");
            if (!string.IsNullOrEmpty(key)) return key;

            key = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            key = Environment.GetEnvironmentVariable("ALIBABA_API_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            key = Environment.GetEnvironmentVariable("ALIBABA_TOKEN_PLAN_KEY");
            if (!string.IsNullOrEmpty(key)) return key;

            return Environment.GetEnvironmentVariable("QWEN_API_KEY");
        }

        private static string ResolveChatEndpoint(string? overrideEndpoint = null)
        {
            string baseEndpoint = !string.IsNullOrWhiteSpace(overrideEndpoint)
                ? overrideEndpoint!
                : DefaultEndpoint;

            string baseAddr = baseEndpoint.TrimEnd('/');
            if (baseAddr.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return baseAddr;
            if (baseAddr.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase))
                return baseAddr + "/chat/completions";
            if (baseAddr.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return baseAddr + "/chat/completions";
            return baseAddr + "/chat/completions";
        }

        // ──────────────────────────────────────────────
        // 모델 목록 조회 (Alibaba Token Plan 2026)
        // ──────────────────────────────────────────────

        public async Task<List<string>> ListModelsAsync()
        {
            string? apiKey = ResolveApiKey();
            string endpoint = DefaultEndpoint.TrimEnd('/') + "/models";

            if (!string.IsNullOrEmpty(apiKey))
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                    using var resp = await _httpClient.SendAsync(req);
                    if (resp.IsSuccessStatusCode)
                    {
                        var json = await resp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        {
                            var models = new List<string>();
                            foreach (var item in data.EnumerateArray())
                            {
                                if (item.TryGetProperty("id", out var id))
                                {
                                    string? idStr = id.GetString();
                                    if (!string.IsNullOrEmpty(idStr)) models.Add(idStr);
                                }
                            }
                            if (models.Count > 0) return models;
                        }
                    }
                }
                catch { }
            }

            return GetDefaultModels();
        }

        public static List<string> GetDefaultModels() => new()
        {
            // Qwen 2026 Fleet
            ModelQwen38Max,
            ModelQwen37Plus,
            ModelQwen37Max,
            ModelQwen36Flash,
            "qwen-image-3.0-pro",
            "qwen-audio-3.0-asr-flash",
            "qwen-audio-3.0-tts-plus",
            "qwen-audio-3.0-realtime-plus",

            // Wan Series
            "wan2.7-image",
            "wan2.7-image-pro",

            // HappyHorse Video Generation Series
            "happyhorse-1.1-i2v",
            "happyhorse-1.1-t2v",
            "happyhorse-1.1-r2v",

            // DeepSeek (Alibaba Token Plan Hosted)
            ModelDeepSeekV4Pro0813,
            ModelDeepSeekV4Pro,
            ModelDeepSeekV4Flash0731,

            // Zhipu AI (Alibaba Token Plan Hosted)
            ModelGlm52,

            // Qwen 2.5 Coder & General Series
            ModelQwen25Coder32B,
            ModelQwen25Coder7B,
            "qwen-coder-plus",
            "qwen-coder-turbo",
            "qwen-max",
            "qwen-plus",
            "qwen-turbo",
            "qwen2.5-72b-instruct",

            DefaultEmbeddingModel
        };

        // ──────────────────────────────────────────────
        // 스트리밍 채팅 (StreamQueryAsync)
        // ──────────────────────────────────────────────

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
            var systemMsg = new { role = "system", content = new SystemPromptBuilder().Build("qwen") };
            var finalMessages = new List<object> { systemMsg };
            finalMessages.AddRange(_messageHistory);

            // 도구 정의
            var tools = _toolRegistry.GetTools();
            var qwenTools = new List<object>();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    object parameters = t.InputSchema ?? (object)new { type = "object", properties = new { }, required = new string[] { } };
                    qwenTools.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = parameters } });
                }
            }

            var payload = new
            {
                model = actualModel,
                messages = finalMessages,
                tools = qwenTools.Any() ? qwenTools : null,
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

                // Thinking / Reasoning Content 처리 (qwen3.8-max, qwen3.7-max, deepseek-v4-pro, glm-5.2)
                if (delta.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    string rText = reasoning.GetString() ?? "";
                    if (!string.IsNullOrEmpty(rText))
                    {
                        finalRes.Text += rText;
                        yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = rText };
                    }
                }

                // 일반 텍스트 델타 처리
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    string text = content.GetString() ?? "";
                    if (!string.IsNullOrEmpty(text))
                    {
                        finalRes.Text += text;
                        yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                    }
                }

                // 도구 호출 누적
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

            // 도구 호출 이벤트 발행
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

            if (string.IsNullOrEmpty(finalRes.Text) && toolCalled)
            {
                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "[italic grey](Executing local tools...)[/]\n" };
            }

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

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            var res = await GetEmbeddingsAsync(new[] { text }, ct);
            return res.FirstOrDefault() ?? Array.Empty<float>();
        }

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
