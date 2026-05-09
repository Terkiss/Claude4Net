using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Dashboard.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Claude4Net.Dashboard.Services;

public class SignalRBroadcaster : IAgentEventBroadcaster
{
    private readonly IHubContext<AgentHub> _hubContext;

    public SignalRBroadcaster(IHubContext<AgentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task BroadcastAsync(IAgentEvent @event)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveEvent", @event);
    }

    public async Task BroadcastApprovalRequestAsync(string requestId, string message)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveApprovalRequest", requestId, message);
    }
}
