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
    /// Anthropic Claude ëª¨ë¸??ê¸°ë°˜?¼ë¡œ ?€??ë°??„êµ¬ ?¬ìš© ?œë¹„?¤ë? ?œê³µ?˜ëŠ” ?„ë¡œë°”ì´??êµ¬í˜„ì²´ì…?ˆë‹¤.
    /// </summary>
    public class ClaudeService : ILLMProvider
    {
        private readonly AnthropicClient _client;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// ClaudeService?????¸ìŠ¤?´ìŠ¤ë¥?ì´ˆê¸°?”í•©?ˆë‹¤.
        /// </summary>
        /// <param name="client">Anthropic API ?´ë¼?´ì–¸??/param>
        /// <param name="toolRegistry">?¬ìš© ê°€?¥í•œ ?„êµ¬ ?ˆì??¤íŠ¸ë¦?/param>
        public ClaudeService(AnthropicClient client, IToolRegistry toolRegistry)
        {
            _client = client;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// ?„ë¡œë°”ì´?”ì˜ ê³ ìœ  ?´ë¦„?…ë‹ˆ??
        /// </summary>
        public string Name => "claude";

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ìš© ? í° ì¹´ìš´?°ë? ê°€?¸ì˜µ?ˆë‹¤.
        /// </summary>
        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        /// <summary>
        /// ?´ë‹¹ ?œê³µ?ì˜ ?„ì¬ ëª¨ë¸ ì»¨í…?¤íŠ¸ ?œí•œ??ê°€?¸ì˜µ?ˆë‹¤. (Claude 3 ê¸°ì? 200k)
        /// </summary>
        public int ContextLimit => 200000;

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬??ë©”ì‹œì§€ë¥?ì¶”ê??©ë‹ˆ??
        /// </summary>
        /// <param name="message">ë©”ì‹œì§€ ê°ì²´</param>
        public void AddMessage(object message) => _messageHistory.Add(message);

        /// <summary>
        /// ?„ì¬ê¹Œì????€???ˆìŠ¤? ë¦¬ë¥?ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <returns>ë©”ì‹œì§€ ê°ì²´ ë¦¬ìŠ¤??/returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// ?€???ˆìŠ¤? ë¦¬ë¥??ˆë¡œ??ëª©ë¡?¼ë¡œ ?€ì²´í•©?ˆë‹¤.
        /// </summary>
        /// <param name="history">?€ì²´í•  ë©”ì‹œì§€ ëª©ë¡</param>
        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null) _messageHistory.AddRange(history);
        }

        /// <summary>
        /// ?¬ìš©??ì¿¼ë¦¬ë¥??¤í–‰?˜ê³  ?‘ë‹µ???¤íŠ¸ë¦¬ë° ë°©ì‹?¼ë¡œ ë°˜í™˜?©ë‹ˆ?? ?„êµ¬ ?¸ì¶œ ë°?ê²°ê³¼ ì²˜ë¦¬ë¥??¬í•¨?©ë‹ˆ??
        /// </summary>
        /// <param name="prompt">?¬ìš©???…ë ¥ ?„ë¡¬?„íŠ¸</param>
        /// <param name="model">?¬ìš©??ëª¨ë¸ëª?(? íƒ ?¬í•­)</param>
        /// <param name="ct">?‘ì—… ì·¨ì†Œ ? í°</param>
        /// <returns>LLM ?¤íŠ¸ë¦??´ë²¤?¸ì˜ ë¹„ë™ê¸??´ê±°??/returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _messageHistory.Add(new { role = "user", content = prompt });
            string actualModel = model ?? AppState.ActiveModel;

            // ?„êµ¬ ?ˆì??¤íŠ¸ë¦¬ì—???„êµ¬ ?•ì˜ ì¶”ì¶œ ë°?Anthropic ?•ì‹?¼ë¡œ ë³€??
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

            // ?œìŠ¤???„ë¡¬?„íŠ¸ êµ¬ì„±
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

            // Anthropic ?¤íŠ¸ë¦??´ë²¤??ì²˜ë¦¬
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
                    // ë©”ì‹œì§€ ì¢…ë£Œ ??ìµœì¢… ê²°ê³¼ ë¹Œë“œ ë°??ˆìŠ¤? ë¦¬ ?…ë°?´íŠ¸
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
