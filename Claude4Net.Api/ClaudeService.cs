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
    public class ClaudeService : ILLMProvider
    {
        private readonly AnthropicClient _client;
        private readonly List<object> _messageHistory = new();

        public ClaudeService(AnthropicClient client) { _client = client; }

        public string Name => "claude";

        public void AddMessage(object message) => _messageHistory.Add(message);

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _messageHistory.Add(new { role = "user", content = prompt });
            string actualModel = model ?? AppState.ActiveModel;

            var payload = new { model = actualModel, max_tokens = 4096, messages = _messageHistory, stream = true };
            var finalResult = new LLMResponse();
            var toolCallsMap = new Dictionary<string, ToolUseRequest>();
            var toolInputsMap = new Dictionary<string, StringBuilder>();

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
                    foreach (var kvp in toolCallsMap)
                    {
                        kvp.Value.Input = JsonSerializer.Deserialize<JsonElement>(toolInputsMap[kvp.Key].ToString());
                        finalResult.ToolCalls.Add(kvp.Value);
                    }
                    _messageHistory.Add(new { role = "assistant", content = new[] { new { type = "text", text = finalResult.Text } } });
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalResult };
                }
            }
        }
    }
}
