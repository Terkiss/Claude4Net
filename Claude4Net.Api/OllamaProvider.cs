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
    /// LLM provider that communicates with a locally running Ollama instance for model inference.
    /// Supports streaming responses, tool calling, and configurable context limits via environment variables.
    /// </summary>
    public class OllamaProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// Initializes a new instance of the <see cref="OllamaProvider"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client for communicating with the Ollama API.</param>
        /// <param name="toolRegistry">The registry managing available tool definitions.</param>
        public OllamaProvider(HttpClient httpClient, IToolRegistry toolRegistry)
        {
            _httpClient = httpClient;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// Gets the unique provider name identifier.
        /// </summary>
        public string Name => "ollama";

        /// <summary>
        /// Gets the token counter for this provider.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// The default context window size for Ollama models (256k tokens).
        /// </summary>
        public const int DefaultContextLimit = 262144;

        /// <summary>
        /// Calculates the effective context limit, considering the OLLAMA_CONTEXT_LIMIT environment variable.
        /// Clamps the value between 8k and 1M tokens for safety.
        /// </summary>
        public static int GetEffectiveContextLimit()
        {
            var envVal = Environment.GetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT");
            if (int.TryParse(envVal, out int limit) && limit > 0)
            {
                return Math.Clamp(limit, 8192, 1048576);
            }
            return DefaultContextLimit;
        }

        /// <summary>
        /// Gets the maximum context window size, defaulting to 256k with environment variable override support.
        /// </summary>
        public int ContextLimit => GetEffectiveContextLimit();


        /// <summary>
        /// Appends a message to the conversation history.
        /// Converts Anthropic-format tool_result messages to Ollama's expected tool response format.
        /// </summary>
        /// <param name="message">The message object to add.</param>
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

                            // Preserve structured data (objects/arrays) by using GetRawText() for JSON serialization
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
        /// Returns the current conversation history as a read-only list.
        /// </summary>
        /// <returns>A read-only list of message objects.</returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// Replaces the entire conversation history with a new set of messages.
        /// </summary>
        /// <param name="history">The new message collection to use as history.</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null) _messageHistory.AddRange(history);
        }

        /// <summary>
        /// Retrieves the list of available models from the Ollama server.
        /// Falls back to a default "llama3" entry if the server is unreachable.
        /// </summary>
        /// <returns>A list of model name strings.</returns>
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
        /// Sends a query to the Ollama API and streams the response asynchronously.
        /// Includes system prompt injection and tool calling support via function-style definitions.
        /// </summary>
        /// <param name="prompt">The user input query to send.</param>
        /// <param name="model">Optional model name (e.g., llama3.1).</param>
        /// <param name="ct">Cancellation token to abort the streaming operation.</param>
        /// <returns>An asynchronous stream of LLM stream events.</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? AppState.ActiveModel;
            string? uri = AuthManager.GetApiKey("ollama") ?? "http://localhost:11434";

            if (!string.IsNullOrEmpty(prompt))
            {
                _messageHistory.Add(new { role = "user", content = prompt });
            }

            // Build tool definitions in OpenAI-compatible function calling format

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

            // Parse response chunks and emit streaming events
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
