using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Dashboard.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Claude4Net.Dashboard.Services;

public class SignalRBroadcaster : IAgentEventBroadcaster
{
    private readonly IHubContext<AgentHub> _hubContext;
    private readonly IHubContext<ControlPlaneHub>? _controlPlaneContext;

    public SignalRBroadcaster(IHubContext<AgentHub> hubContext, IHubContext<ControlPlaneHub>? controlPlaneContext = null)
    {
        _hubContext = hubContext;
        _controlPlaneContext = controlPlaneContext;
    }

    public async Task BroadcastAsync(IAgentEvent @event)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveEvent", @event);
        if (_controlPlaneContext != null)
        {
            await _controlPlaneContext.Clients.All.SendAsync("ReceiveAgentEvent", @event);
            await _controlPlaneContext.Clients.All.SendAsync("ReceiveLiveTelemetry", @event);
        }
    }

    public async Task BroadcastApprovalRequestAsync(string requestId, string message)
    {
        await _hubContext.Clients.All.SendAsync("ReceiveApprovalRequest", requestId, message);
        if (_controlPlaneContext != null)
        {
            await _controlPlaneContext.Clients.All.SendAsync("ReceiveApprovalRequest", requestId, message);
        }
    }
}
