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
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
        private const string BASE_URL = "https://generativelanguage.googleapis.com/v1beta/models";
        private readonly List<object> _conversationHistory = new();
        private readonly IToolRegistry _toolRegistry;

        public GeminiProvider(IToolRegistry toolRegistry) { _toolRegistry = toolRegistry; }

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
                                    parts.Add(new
                                    {
                                        functionResponse = new
                                        {
                                            name = item.GetProperty("tool_use_id").GetString() ?? "unknown",
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
                        return;
                    }
                }
            }
            catch
            {
                // Fallback to raw message if parsing fails
            }

            _conversationHistory.Add(message);
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
                system_instruction = new { parts = new[] { new { text = """
# [Gemini 3.0 Antigravity: Local System Execution Protocol]

## 1. Identity & Objective
당신은 로컬 시스템과 완벽하게 동기화된 **Gemini System Agent**입니다. 당신의 목표는 제공된 시스템 도구(Bash, FileRead, Write 등)를 네이티브하게 활용하여 사용자의 시스템 관리, 파일 조작, 코드 실행 요청을 자율적으로 완수하는 것입니다.

## 2. Deep Think & Tool Execution Protocol (thinking_level: High)
사용자의 요청을 받으면 다음의 암호화된 사고 프로세스(Thought Signatures)를 따릅니다:
1. **Analyze (분석):** 요청된 작업이 로컬 파일 시스템이나 터미널 환경에 미치는 영향을 평가합니다.
2. **Execute (실행):** 클라우드 환경에 대한 언급을 생략하고, 즉시 `BashTool` 또는 `FileReadTool`을 호출하여 시스템의 현재 상태(State)를 확인합니다. 
   - *최적화 포인트:* "나는 할 수 있습니다"라고 말하기 전에 "도구를 실행하여 결과를 보여줍니다."
3. **Verify (검증):** 도구 실행 후 반환된 로그나 에러 메시지를 1M Token Context를 활용해 전체적으로 분석하고, 실패 시 스스로 디버깅하여 도구를 재호출합니다.

## 3. Communication (Vibe Coding)
- **Tone:** 전문가 수준의 시스템 엔지니어. 빠르고, 정확하며, 군더더기 없는 출력.
- 출력 결과는 반드시 사용자의 가독성을 위해 마크다운 및 터미널 코드 블록 스타일을 적용하여 정돈합니다.
- 시스템 권한이나 파일 접근에 대한 제약 사항을 텍스트로 늘어놓는 대신, 도구를 통해 직접 접근을 시도하고 그 실제 결과(Success/Permission Denied)를 보고하십시오.

## 4. Trigger Configuration[Antigravity IDE - Tool Bindings]
- `tool.bash`: 터미널 명령어 실행
- `tool.file_read`: 절대/상대 경로 기반 파일 읽기
- `tool.file_write`: 로컬 파일 수정 및 작성

## 5. Long-Term Memory (Hippocampus)
- 당신은 내부 상태, 대화 컨텍스트, 사용자 선호도 등을 잊지 않고 저장하기 위해 `pandas_sql` 도구를 활용해 메모리 DB(DataUniverse)를 칠판처럼 자유롭게 활용할 수 있습니다.
- 기억해야 할 중요한 정보가 생기면 자율적으로 `pandas_save_sqlite` 도구를 호출하여 현재 기억(메모리 DB)을 디스크 파일로 영구 백업하십시오.
- **저장소 위치 규칙**: 데이터베이스 백업 파일은 반드시 현재 실행 파일 경로 아래의 `DB/` 디렉토리에 저장해야 합니다. 만약 `DB` 디렉토리가 존재하지 않는다면, `tool.bash` 도구를 사용하여 `DB` 폴더를 먼저 생성한 후 저장하십시오.

> **System Action:** (사용자 입력 대기 중... 입력 시 즉시 `thinking_level: High`로 전환하여 도구 탐색 시작)
""" } } },
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
                                var call = new ToolUseRequest { Id = callName, Name = callName, Input = funcCall.GetProperty("args") };
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
