using Microsoft.AspNetCore.SignalR;
using Claude4Net.SDK.Events;

namespace Claude4Net.Dashboard.Hubs;

public class AgentHub : Hub
{
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
        // This will be called from the Blazor client to notify the server/runtime
        // We'll need a way to link this back to the waiting ApprovalHandler
        await Clients.All.SendAsync("ApprovalResponded", requestId, approved, reason);
    }
}
