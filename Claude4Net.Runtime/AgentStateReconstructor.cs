using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class AgentStateReconstructor
    {
        public class ReconstructedState
        {
            public List<object> History { get; set; } = new();
            public string CurrentTask { get; set; } = string.Empty;
            public long LastVersion { get; set; }
        }

        public static ReconstructedState Reconstruct(IEnumerable<IAgentEvent> events, AgentStateSnapshot? initialSnapshot = null)
        {
            var state = new ReconstructedState();
            if (initialSnapshot != null)
            {
                state.History = new List<object>(initialSnapshot.History);
                state.CurrentTask = initialSnapshot.CurrentTask;
                state.LastVersion = initialSnapshot.LastVersion;
            }

            foreach (var @event in events.OrderBy(e => e.Version))
            {
                Apply(state, @event);
                state.LastVersion = @event.Version;
            }

            return state;
        }

        private static void Apply(ReconstructedState state, IAgentEvent @event)
        {
            switch (@event)
            {
                case UserPromptReceivedEvent userPrompt:
                    state.CurrentTask = userPrompt.Prompt;
                    state.History.Add(new { role = "user", content = userPrompt.Prompt });
                    break;

                case AgentThoughtEvent thought:
                    // Usually thoughts are not added to provider history directly as separate messages
                    // unless the provider supports it. Here we just track it.
                    break;

                case ToolCalledEvent toolCall:
                    // In some protocols, tool calls are part of the assistant message.
                    // For reconstruction, we might need to be careful about how we map this back.
                    // Simplified: We assume assistant's response that includes tool calls is handled by FinalResponseGenerated
                    // OR we reconstruct the tool calls list.
                    break;

                case ToolResultEvent toolResult:
                    state.History.Add(new
                    {
                        role = "user",
                        content = new[] {
                            new {
                                type = "tool_result",
                                tool_use_id = toolResult.ToolUseId,
                                content = toolResult.Result,
                                is_error = toolResult.IsError
                            }
                        }
                    });
                    break;

                case FinalResponseGeneratedEvent finalResponse:
                    state.History.Add(new { role = "assistant", content = finalResponse.Response });
                    break;
            }
        }
    }
}
