using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// LLM provider implementation that communicates with the Anthropic Claude API.
    /// Supports streaming responses, tool use, and maintains conversation history.
    /// </summary>
    public class ClaudeService : ILLMProvider
    {
        private readonly AnthropicClient _client;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClaudeService"/> class.
        /// </summary>
        /// <param name="client">The Anthropic API client for HTTP communication.</param>
        /// <param name="toolRegistry">The registry providing available tool definitions.</param>
        public ClaudeService(AnthropicClient client, IToolRegistry toolRegistry)
        {
            _client = client;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// Gets the unique provider name identifier.
        /// </summary>
        public string Name => "claude";

        /// <summary>
        /// Gets the token counter for this provider.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// Gets the maximum context window size for this provider (200k tokens for Claude 3).
        /// </summary>
        public int ContextLimit => 200000;

        /// <summary>
        /// Appends a message to the conversation history.
        /// </summary>
        /// <param name="message">The message object to add.</param>
        public void AddMessage(object message) => _messageHistory.Add(message);

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
        /// Executes a user query and streams the response asynchronously.
        /// Handles tool call detection, JSON input accumulation, and conversation history updates.
        /// </summary>
        /// <param name="prompt">The user input prompt to send to Claude.</param>
        /// <param name="model">Optional model name override. Defaults to the active model from AppState.</param>
        /// <param name="ct">Cancellation token to abort the streaming operation.</param>
        /// <returns>An asynchronous stream of LLM stream events including text deltas and tool calls.</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _messageHistory.Add(new { role = "user", content = prompt });
            string actualModel = model ?? AppState.ActiveModel;

            // Extract tool definitions from the registry and convert to Anthropic format
            var tools = _toolRegistry.GetTools();
            var anthropicTools = new List<object>();
            if (tools != null)
            {
                foreach (var t in tools)
                {
                    object parameters = t.InputSchema ?? (object)new { type = "object", properties = new { }, required = new string[] { } };
                    anthropicTools.Add(new { name = t.Name, description = t.Description, input_schema = parameters });
                }
            }

            // Build the system prompt
            string systemPrompt = new SystemPromptBuilder().Build("claude");

            var payload = new
            {
                model = actualModel,
                max_tokens = 4096,
                system = systemPrompt,
                messages = _messageHistory,
                tools = anthropicTools.Any() ? anthropicTools : null,
                stream = true
            };
            var finalResult = new LLMResponse();
            var toolCallsMap = new Dictionary<string, ToolUseRequest>();
            var toolInputsMap = new Dictionary<string, StringBuilder>();

            // Process Anthropic streaming events
            await foreach (var evt in _client.CreateMessageStreamAsync(payload, ct))
            {
                if (evt.Type == "content_block_start")
                {
                    var block = evt.Data.GetProperty("content_block");
                    if (block.GetProperty("type").GetString() == "tool_use")
                    {
                        var toolCall = new ToolUseRequest { Id = block.GetProperty("id").GetString()!, Name = block.GetProperty("name").GetString()! };
                        toolCallsMap[toolCall.Id] = toolCall;
                        toolInputsMap[toolCall.Id] = new StringBuilder();
                        yield return new LLMStreamEvent { Type = LLMStreamEventType.ToolCallStart, ToolCall = toolCall };
                    }
                }
                else if (evt.Type == "content_block_delta")
                {
                    var delta = evt.Data.GetProperty("delta");
                    string type = delta.GetProperty("type").GetString()!;
                    if (type == "text_delta")
                    {
                        string text = delta.GetProperty("text").GetString()!;
                        finalResult.Text += text;
                        yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                    }
                    else if (type == "input_json_delta")
                    {
                        int index = evt.Data.GetProperty("index").GetInt32();
                        var toolId = toolCallsMap.Keys.ElementAtOrDefault(index);
                        if (toolId != null) toolInputsMap[toolId].Append(delta.GetProperty("partial_json").GetString());
                    }
                }
                else if (evt.Type == "message_stop")
                {
                    // Build the final result and update the conversation history on message completion
                    var assistantContent = new List<object>();
                    if (!string.IsNullOrEmpty(finalResult.Text))
                    {
                        assistantContent.Add(new { type = "text", text = finalResult.Text });
                    }

                    foreach (var kvp in toolCallsMap)
                    {
                        kvp.Value.Input = JsonSerializer.Deserialize<JsonElement>(toolInputsMap[kvp.Key].ToString());
                        finalResult.ToolCalls.Add(kvp.Value);
                        assistantContent.Add(new
                        {
                            type = "tool_use",
                            id = kvp.Value.Id,
                            name = kvp.Value.Name,
                            input = kvp.Value.Input
                        });
                    }

                    _messageHistory.Add(new { role = "assistant", content = assistantContent.ToArray() });
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalResult };
                }
            }
        }
    }
}
