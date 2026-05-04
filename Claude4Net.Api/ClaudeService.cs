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
    /// Anthropic Claude 모델을 기반으로 대화 및 도구 사용 서비스를 제공하는 프로바이더 구현체입니다.
    /// </summary>
    public class ClaudeService : ILLMProvider
    {
        private readonly AnthropicClient _client;
        private readonly List<object> _messageHistory = new();
        private readonly IToolRegistry _toolRegistry;

        /// <summary>
        /// ClaudeService의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="client">Anthropic API 클라이언트</param>
        /// <param name="toolRegistry">사용 가능한 도구 레지스트리</param>
        public ClaudeService(AnthropicClient client, IToolRegistry toolRegistry) 
        { 
            _client = client;
            _toolRegistry = toolRegistry;
        }

        /// <summary>
        /// 프로바이더의 고유 이름입니다.
        /// </summary>
        public string Name => "claude";

        /// <summary>
        /// 대화 히스토리에 메시지를 추가합니다.
        /// </summary>
        /// <param name="message">메시지 객체</param>
        public void AddMessage(object message) => _messageHistory.Add(message);

        /// <summary>
        /// 현재까지의 대화 히스토리를 반환합니다.
        /// </summary>
        /// <returns>메시지 객체 리스트</returns>
        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        /// <summary>
        /// 사용자 쿼리를 실행하고 응답을 스트리밍 방식으로 반환합니다. 도구 호출 및 결과 처리를 포함합니다.
        /// </summary>
        /// <param name="prompt">사용자 입력 프롬프트</param>
        /// <param name="model">사용할 모델명 (선택 사항)</param>
        /// <param name="ct">작업 취소 토큰</param>
        /// <returns>LLM 스트림 이벤트의 비동기 열거자</returns>
        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            _messageHistory.Add(new { role = "user", content = prompt });
            string actualModel = model ?? AppState.ActiveModel;

            // 도구 레지스트리에서 도구 정의 추출 및 Anthropic 형식으로 변환
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

            // 시스템 프롬프트 구성
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

            // Anthropic 스트림 이벤트 처리
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
                    // 메시지 종료 시 최종 결과 빌드 및 히스토리 업데이트
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
