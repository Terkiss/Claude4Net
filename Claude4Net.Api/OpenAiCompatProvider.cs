using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// OpenAI 규격(/v1/chat/completions)에 맞춰 작동하는 범용 OpenAI 호환 LLM 프로바이더 구현체입니다. (LM Studio 등 지원)
    /// </summary>
    public class OpenAiCompatProvider : ILLMProvider, IEmbeddingProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly ProviderDescriptor _descriptor;
        private readonly List<object> _messageHistory = new();

        public OpenAiCompatProvider(HttpClient httpClient, IToolRegistry toolRegistry, ProviderDescriptor descriptor)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public string Name => _descriptor.Id;

        public string ProviderId => _descriptor.Id;

        public string ModelId => _descriptor.DefaultModels.Small;

        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        public int ContextLimit => _descriptor.ContextWindowSize > 0 ? _descriptor.ContextWindowSize : 200000;

        public async Task<List<string>> ListModelsAsync()
        {
            string endpoint = _descriptor.Endpoint;
            string? apiKey = null;

            if (_descriptor.Auth != null && _descriptor.Auth.Mode.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var envVar in _descriptor.Auth.EnvVars)
                {
                    apiKey = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(apiKey)) break;
                }
                if (string.IsNullOrEmpty(apiKey)) apiKey = AuthManager.GetApiKey(_descriptor.Id);
                if (string.IsNullOrEmpty(apiKey) && _descriptor.Auth.EnvVars.Count > 0) apiKey = AuthManager.GetApiKey(_descriptor.Auth.EnvVars[0]);
            }

            if (!endpoint.Contains("/chat/completions"))
            {
                string baseAddr = endpoint.TrimEnd('/');
                endpoint = baseAddr.EndsWith("/v1") ? baseAddr + "/models" : baseAddr + "/v1/models";
            }
            else
            {
                endpoint = endpoint.Replace("/chat/completions", "/models");
            }

            Uri endpointUri = ProviderEndpointPolicy.ParseAndValidate(endpoint, nameof(ProviderDescriptor.Endpoint));
            using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await _httpClient.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();
                var jsonStr = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(jsonStr);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var models = new List<string>();
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var id)) models.Add(id.GetString()!);
                    }
                    return models;
                }
                return new List<string>();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch models from LM Studio: {ex.Message}", ex);
            }
        }

        public void AddMessage(object message)
        {
            if (message != null)
            {
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

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
            string prompt,
            string? model = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? _descriptor.DefaultModels.Large;
            if (string.IsNullOrEmpty(actualModel))
            {
                actualModel = _descriptor.DefaultModels.Small;
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                _messageHistory.Add(new { role = "user", content = prompt });
            }

            var systemPrompt = new SystemPromptBuilder().Build(_descriptor.Id);
            var systemMsg = new { role = "system", content = systemPrompt };

            var finalMessages = new List<object> { systemMsg };
            finalMessages.AddRange(_messageHistory);

            var tools = _toolRegistry.GetTools();
            var openAiTools = new List<object>();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    object parameters = t.InputSchema ?? (object)new { type = "object", properties = new { }, required = new string[] { } };
                    openAiTools.Add(new { type = "function", function = new { name = t.Name, description = t.Description, parameters = parameters } });
                }
            }

            var payload = new
            {
                model = actualModel,
                messages = finalMessages,
                tools = openAiTools.Any() ? openAiTools : null,
                stream = true
            };

            string endpoint = _descriptor.Endpoint;
            string? apiKey = null;

            // Set Authentication
            if (_descriptor.Auth != null && _descriptor.Auth.Mode.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var envVar in _descriptor.Auth.EnvVars)
                {
                    apiKey = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(apiKey)) break;
                }
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = AuthManager.GetApiKey(_descriptor.Id);
                }
                if (string.IsNullOrEmpty(apiKey) && _descriptor.Auth.EnvVars.Count > 0)
                {
                    apiKey = AuthManager.GetApiKey(_descriptor.Auth.EnvVars[0]);
                }
            }

            if (!endpoint.Contains("/chat/completions"))
            {
                string baseAddr = endpoint.TrimEnd('/');
                endpoint = baseAddr.EndsWith("/v1") ? baseAddr + "/chat/completions" : baseAddr + "/v1/chat/completions";
            }

            var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            Uri endpointUri = ProviderEndpointPolicy.ParseAndValidate(endpoint, nameof(ProviderDescriptor.Endpoint));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = JsonContent.Create(payload, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), jsonOptions)
            };

            // Set Headers
            if (_descriptor.Headers != null)
            {
                foreach (var header in _descriptor.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

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
                if (dataLine.StartsWith("data: "))
                {
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

                    if (chunk.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta))
                        {
                            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                            {
                                string text = content.GetString() ?? "";
                                if (!string.IsNullOrEmpty(text))
                                {
                                    finalRes.Text += text;
                                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                                }
                            }

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
                    }
                }
            }

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
                assistantToolCalls.Add(new { id = call.Id, type = "function", function = new { name = call.Name, arguments = argsStr } });
                yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = call };
            }

            if (string.IsNullOrEmpty(finalRes.Text) && toolCalled) yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "[italic grey](Executing local tools...)[/]\n" };

            _messageHistory.Add(new { role = "assistant", content = string.IsNullOrEmpty(finalRes.Text) ? null : finalRes.Text, tool_calls = assistantToolCalls.Any() ? assistantToolCalls : null });
            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalRes };
        }

        public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            var res = await GetEmbeddingsAsync(new[] { text }, ct);
            return res.FirstOrDefault() ?? Array.Empty<float>();
        }

        public async Task<List<float[]>> GetEmbeddingsAsync(IEnumerable<string> texts, CancellationToken ct = default)
        {
            if (texts == null || !texts.Any()) return new List<float[]>();

            string endpoint = _descriptor.Endpoint;
            string? apiKey = null;

            if (_descriptor.Auth != null && _descriptor.Auth.Mode.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var envVar in _descriptor.Auth.EnvVars)
                {
                    apiKey = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(apiKey)) break;
                }
                if (string.IsNullOrEmpty(apiKey)) apiKey = AuthManager.GetApiKey(_descriptor.Id);
                if (string.IsNullOrEmpty(apiKey) && _descriptor.Auth.EnvVars.Count > 0) apiKey = AuthManager.GetApiKey(_descriptor.Auth.EnvVars[0]);
            }

            if (!endpoint.Contains("/embeddings"))
            {
                if (endpoint.Contains("/chat/completions"))
                    endpoint = endpoint.Replace("/chat/completions", "/embeddings");
                else if (endpoint.Contains("/models"))
                    endpoint = endpoint.Replace("/models", "/embeddings");
                else
                {
                    string baseAddr = endpoint.TrimEnd('/');
                    endpoint = baseAddr.EndsWith("/v1") ? baseAddr + "/embeddings" : baseAddr + "/v1/embeddings";
                }
            }

            string model = _descriptor.DefaultModels.Small;
            if (string.IsNullOrEmpty(model)) model = "text-embedding-nomic"; // fallback

            var payload = new
            {
                model = model,
                input = texts.ToArray()
            };

            var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            Uri endpointUri = ProviderEndpointPolicy.ParseAndValidate(endpoint, nameof(ProviderDescriptor.Endpoint));
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = JsonContent.Create(payload, new System.Net.Http.Headers.MediaTypeHeaderValue("application/json"), jsonOptions)
            };

            if (_descriptor.Headers != null)
            {
                foreach (var header in _descriptor.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var jsonStr = await response.Content.ReadAsStringAsync();
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
