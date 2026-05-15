using System;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Rendering;

namespace Claude4Net.Cli.Ui;

/// <summary>
/// Bridge between neutral AgentRunEvent observation system and Lumen UI state.
/// </summary>
public class LumenRunObserver(LumenRenderer renderer, LumenState initialState) : IAgentRunObserver
{
    private LumenState _state = initialState;

    /// <summary>
    /// Gets the current UI state.
    /// </summary>
    public LumenState State => _state;

    /// <summary>
    /// Processes an agent run-time event and updates the UI state.
    /// </summary>
    public async Task OnEventAsync(IAgentRunEvent e)
    {
        if (e == null) return;

        try
        {
            LumenEvent? lumenEvent = e switch
            {
                SDK.RunStartedEvent r => new Events.RunStartedEvent(r.Provider, r.Model, r.SessionId),
                SDK.ThinkingStartedEvent => new Events.ThinkingStartedEvent(),
                SDK.ThinkingDeltaEvent d => new Events.ThinkingUpdatedEvent(d.Delta),
                SDK.TextDeltaEvent d => new Events.AssistantTextUpdatedEvent(d.Delta),
                SDK.ToolCallQueuedEvent t => new Events.ToolCallStartedEvent(t.ToolCallId, t.ToolName, t.Arguments),
                SDK.ToolResultReceivedEvent tr => new Events.ToolResultReceivedEvent(tr.ToolCallId, tr.Content?.ToString() ?? "", tr.IsError),
                SDK.RunErrorEvent err => new Events.ErrorReceivedEvent(err.ErrorMessage),
                SDK.RunCompletedEvent => new Events.RunCompletedEvent(),
                _ => null
            };

            if (lumenEvent != null)
            {
                UpdateState(lumenEvent);
            }
        }
        catch (Exception ex)
        {
            // Fail-safe: UI mapping or state reduction errors should not crash the core loop.
            Console.Error.WriteLine($"[LumenUI Error] Error mapping AgentRunEvent: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Manually updates the state with a Lumen event.
    /// Useful for bridges that don't use IAgentRunEvent (e.g. IOutputHandler).
    /// </summary>
    public void UpdateState(LumenEvent lumenEvent)
    {
        try
        {
            _state = LumenReducer.Reduce(_state, lumenEvent);
            renderer.RenderAppend(_state);
        }
        catch (Exception ex)
        {
            // Fail-safe: State reduction or rendering errors should not crash the core loop.
            Console.Error.WriteLine($"[LumenUI Error] Error updating state or rendering: {ex.Message}");
        }
    }
}
