using Microsoft.AspNetCore.SignalR;
using Claude4Net.SDK.Events;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using System.Text.Json;

namespace Claude4Net.Dashboard.Hubs;

public class AgentHub : Hub
{
    public AgentHub()
    {
    }

    public async Task<InitialStateRecord> GetInitialState()
    {
        // Use AppState to determine workspace for EventStore at the time of call
        string ws = AppState.CurrentCwd ?? AppState.SystemBaseDir;
        var eventStore = new FileAgentEventStore(ws);

        var tasks = AppState.GetCoordinatedTasks().ToList();
        var sessionId = AppState.SessionId;

        // Load recent events (last 50)
        var events = await eventStore.GetEventsAsync(sessionId);
        var recentEvents = events.TakeLast(50).Select(e => (object)e).ToList();

        return new InitialStateRecord
        {
            SessionId = sessionId,
            Workspace = ws,
            Provider = AppState.ActiveProvider,
            Model = AppState.ActiveModel,
            PermissionMode = AppState.CurrentPermissionMode.ToString(),
            Tasks = tasks,
            RecentEvents = recentEvents
        };
    }


    public async Task SendEvent(AgentEventBase agentEvent)
    {
        await Clients.All.SendAsync("ReceiveEvent", agentEvent);
    }

    public async Task SendApprovalRequest(string requestId, string message)
    {
        await Clients.All.SendAsync("ReceiveApprovalRequest", requestId, message);
    }

    public async Task RespondToApproval(string requestId, bool approved, string? reason)
    {
        // Register the decision in IdempotentApprovalEngine for thread safety and idempotency
        if (!IdempotentApprovalEngine.TryRegisterDecision(requestId, approved, reason, out var errorMsg))
        {
            if (errorMsg != null)
            {
                // Conflict detected!
                Console.WriteLine($"[ERROR] Concurrency Conflict: {errorMsg}");
                await Clients.All.SendAsync("ApprovalConflictDetected", requestId, errorMsg);
                throw new HubException(errorMsg);
            }
        }

        await Clients.All.SendAsync("ApprovalResponded", requestId, approved, reason);
    }
}

public record InitialStateRecord
{
    public string SessionId { get; init; } = "";
    public string Workspace { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string PermissionMode { get; init; } = "";
    public List<CoordinateTask> Tasks { get; init; } = new();
    public List<object> RecentEvents { get; init; } = new();
}
