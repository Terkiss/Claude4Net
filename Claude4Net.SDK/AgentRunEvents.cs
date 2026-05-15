using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    /// <summary>
    /// Marker interface for all agent run-time events.
    /// </summary>
    public interface IAgentRunEvent { }

    /// <summary>
    /// Observer interface to receive structured agent execution events.
    /// This allows UI components or external systems to monitor agent progress without depending on the console.
    /// </summary>
    public interface IAgentRunObserver
    {
        /// <summary>
        /// Called when an agent run-time event occurs.
        /// </summary>
        Task OnEventAsync(IAgentRunEvent e);
    }

    /// <summary>
    /// Implementation of IAgentRunObserver that does nothing.
    /// </summary>
    public class NullAgentRunObserver : IAgentRunObserver
    {
        /// <summary>
        /// Shared instance of the null observer.
        /// </summary>
        public static NullAgentRunObserver Instance { get; } = new();
        
        /// <inheritdoc />
        public Task OnEventAsync(IAgentRunEvent e) => Task.CompletedTask;
    }

    /// <summary> Fired when a new agent run starts. </summary>
    public record RunStartedEvent(string SessionId, string Provider, string Model, string Prompt) : IAgentRunEvent;
    
    /// <summary> Fired when the smart router selects a provider and model. </summary>
    public record RoutingSelectedEvent(string Provider, string Model, string Reason) : IAgentRunEvent;
    
    /// <summary> Fired when the agent begins a new thinking turn. </summary>
    public record ThinkingStartedEvent(int TurnCount) : IAgentRunEvent;
    
    /// <summary> Fired when a thinking delta (internal reasoning) is received. </summary>
    public record ThinkingDeltaEvent(string Delta) : IAgentRunEvent;
    
    /// <summary> Fired when a text delta (content response) is received. </summary>
    public record TextDeltaEvent(string Delta) : IAgentRunEvent;
    
    /// <summary> Fired when a tool call is queued for execution. </summary>
    public record ToolCallQueuedEvent(string ToolCallId, string ToolName, string Arguments) : IAgentRunEvent;
    
    /// <summary> Fired when a tool execution result is received. </summary>
    public record ToolResultReceivedEvent(string ToolCallId, object? Content, bool IsError) : IAgentRunEvent;
    
    /// <summary> Fired when the assistant finishes a complete response turn. </summary>
    public record AssistantMessageCompletedEvent(string FullResponse) : IAgentRunEvent;
    
    /// <summary> Fired when an error occurs during the run. </summary>
    public record RunErrorEvent(string ErrorMessage) : IAgentRunEvent;
    
    /// <summary> Fired when the agent run completes (successfully or with error). </summary>
    public record RunCompletedEvent(string SessionId, TimeSpan Duration) : IAgentRunEvent;
}
