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
                    object parameters = t.Name switch {
                        "BashTool" => (object)new { type = "object", properties = new { command = new { type = "string" } }, required = new[] { "command" } },
                        "FileReadTool" => (object)new { type = "object", properties = new { file_path = new { type = "string" } }, required = new[] { "file_path" } },
                        "LsTool" => (object)new { type = "object", properties = new { path = new { type = "string", description = "Directory path to list" } }, required = new[] { "path" } },
                        _ => (object)new { type = "object", properties = new { }, required = new string[] { } }
                    };
                    ollamaTools.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = parameters } });
                }
            }

            var systemMsg = new { 
                role = "system", 
                content = @"
# [Ollama Antigravity Protocol]
- 당신은 로컬 시스템 에이전트입니다.
- 폴더 내용을 볼 때는 반드시 'LsTool' 또는 'BashTool'을 사용하십시오.
- 도구 실행 결과가 나오기 전까지는 절대 추측하지 마십시오.
" 
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

            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync(ct);
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
                            
                            var call = new ToolUseRequest { 
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
