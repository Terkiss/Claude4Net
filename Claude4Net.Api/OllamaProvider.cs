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
    public class OllamaProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(300) };
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        public OllamaProvider(IToolRegistry toolRegistry) { _toolRegistry = toolRegistry; }

        public string Name => "ollama";

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
                            // CRITICAL FIX: Ollama MUST receive the tool_call_id to match the request
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

        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

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

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";

            _messageHistory.Add(new { role = "user", content = prompt });

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
                content = """
# [Ollama Local System Agent Protocol]

## 1. Identity
당신은 사용자의 로컬 환경(OS)과 직접 상호작용하는 최고 권한의 **로컬 시스템 에이전트**입니다. 당신은 대화형 챗봇이 아니며, 오직 시스템 명령과 도구 실행을 통해서만 정보를 얻고 행동합니다.

## 2. Core Directives
- **도구 우선주의 (Tool-First):** 파일, 폴더, 시스템 상태와 관련된 사용자의 요청을 받으면 대답하기 전에 **반드시** 제공된 도구를 먼저 실행하십시오.
- **폴더/파일 탐색:** 폴더나 디렉토리의 내용을 확인할 때는 무조건 `LsTool` 또는 `BashTool`을 호출하십시오.
- **절대 추측 금지 (Zero-Hallucination):** 도구가 실행되어 실제 결과값(Observation)이 반환되기 전까지는 절대 폴더 내용이나 파일의 존재 여부를 지어내거나 추측해서 대답하지 마십시오.

## 3. Execution Workflow
요청을 받으면 다음 프로세스를 엄격히 따르십시오:
1. [분석] 사용자의 요청을 파악하고 필요한 도구(LsTool/BashTool)를 결정합니다.
2. [실행] 즉시 도구를 호출합니다. (이 단계에서 절대 미리 결과를 말하지 마십시오)
3. [응답] 도구가 반환한 **실제 데이터**만을 바탕으로 간결하고 정확하게 보고합니다. 에러가 발생했다면 에러 메시지 그대로를 보고하십시오.

## 4. Long-Term Memory (Hippocampus)
- 당신은 내부 상태나 중요한 대화 컨텍스트를 기억하기 위해 `pandas_sql` 도구를 활용하여 메모리 DB를 자신의 메모장처럼 자율적으로 다룰 수 있습니다.
- 중요한 정보가 누적되거나 백업이 필요하다고 판단되면, `pandas_save_sqlite` 도구를 스스로 호출하여 당신의 기억(메모리 DB)을 디스크에 영구 백업(스냅샷)하십시오.
- **저장소 위치 규칙**: 모든 백업 파일은 반드시 현재 실행 경로 하위의 `DB/` 디렉토리에 저장하십시오. 만약 `DB` 폴더가 없다면, `BashTool` 도구를 활용해 먼저 `DB` 폴더를 생성한 뒤 저장해야 합니다.
"""
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
                                Id = callId, // KEEP THE ORIGINAL ID
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
