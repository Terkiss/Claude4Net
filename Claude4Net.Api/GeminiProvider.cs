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
    /// Google Gemini API를 활용하여 대화형 AI 및 도구 호출 기능을 제공하는 프로바이더입니다.
    /// Anthropic 형식의 메시지를 Gemini 규격으로 변환하여 상호 호환성을 유지합니다.
    /// </summary>
    public class GeminiProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;
        private readonly Dictionary<string, string> _toolCallIdToNameMap = new();

        /// <summary>
        /// GeminiProvider의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="httpClient">HTTP 요청을 위한 클라이언트</param>
        /// <param name="toolRegistry">도구 등록 정보를 관리하는 레지스트리</param>
        public GeminiProvider(HttpClient httpClient, IToolRegistry toolRegistry) 
        { 
            _httpClient = httpClient;
            _toolRegistry = toolRegistry; 
        }

        /// <summary>
        /// 프로바이더의 고유 이름입니다.
        /// </summary>
        public string Name => "gemini";

        /// <summary>
        /// 대화 히스토리에 메시지를 추가하며, Anthropic 형식을 Gemini 형식으로 변환합니다.
        /// </summary>
        /// <param name="message">추가할 메시지 객체 (Anthropic 규격 선호)</param>
        public void AddMessage(object message)
        {
            if (message == null) return;

            try
            {
                var json = JsonSerializer.Serialize(message);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Anthropic 메시지를 Gemini 형식으로 변환 시도
                if (root.TryGetProperty("role", out var roleProp))
                {
                    string role = roleProp.GetString() ?? "user";

                    if (root.TryGetProperty("content", out var contentProp))
                    {
                        var parts = new List<object>();

                        if (contentProp.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in contentProp.EnumerateArray())
                            {
                                if (item.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "tool_result")
                                {
                                    // 도구 실행 결과 변환
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

                        string geminiRole = (parts.Any(p => json.Contains("functionResponse"))) ? "function" : role;
                        _conversationHistory.Add(new { role = geminiRole, parts = parts });
                        ApplySlidingWindow();
                        return;
                    }
                }
            }
            catch
            {
                // 변환 실패 시 원본 메시지 추가
            }

            _conversationHistory.Add(message);
            ApplySlidingWindow();
        }

        /// <summary>
        /// 슬라이딩 윈도우 방식으로 최근 대화 맥락만 유지합니다.
        /// </summary>
        private void ApplySlidingWindow()
        {
            const int MAX_HISTORY = 16; // 약 8회의 턴 유지
            if (_conversationHistory.Count > MAX_HISTORY)
            {
                int toRemove = _conversationHistory.Count - MAX_HISTORY;
                _conversationHistory.RemoveRange(0, toRemove);
            }
        }

        /// <summary>
        /// 현재 대화 히스토리를 반환합니다.
        /// </summary>
        /// <returns>메시지 객체 리스트</returns>
        public IReadOnlyList<object> GetHistory() => _conversationHistory.AsReadOnly();

        /// <summary>
        /// Gemini API를 호출하여 결과를 스트리밍합니다. 시스템 프롬프트 및 도구 정의가 포함됩니다.
        /// </summary>
        /// <param name="prompt">사용자 입력 쿼리</param>
        /// <param name="model">모델명 (예: gemini-1.5-pro)</param>
        /// <param name="ct">작업 취소 토큰</param>
        /// <returns>스트리밍 이벤트 열거자</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? apiKey = AuthManager.GetGeminiApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new Exception("Gemini API key is missing.");

            if (!string.IsNullOrEmpty(prompt))
            {
                _conversationHistory.Add(new { role = "user", parts = new[] { new { text = prompt } } });
            }

            // 도구 선언 (Function Declarations) 구성
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

            // 생성 설정 (Thinking Config 지원 포함)
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

            // SSE 스트림 파싱
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

                    // 안전 필터링 처리
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
                                // 도구 호출 처리
                                string callName = funcCall.GetProperty("name").GetString()!;
                                string callId = $"{callName}_{toolCallIndex++}";
                                var call = new ToolUseRequest { Id = callId, Name = callName, Input = funcCall.GetProperty("args").Clone() };
                                
                                _toolCallIdToNameMap[callId] = callName;
                                
                                toolCalls.Add(call);
                                assistantParts.Add(part.Clone());
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
