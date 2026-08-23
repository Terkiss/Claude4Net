using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Claude4Net.SDK.Events;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Dashboard.Client.Models;
using System.Text.Json;

namespace Claude4Net.Dashboard.Hubs;

public class AgentHub : Hub
{
    private readonly ControlPlaneHub _controlPlane;

    public AgentHub()
    {
        _controlPlane = new ControlPlaneHub();
    }

    public async Task<InitialStateRecord> GetInitialState()
    {
        string ws = AppState.CurrentCwd ?? AppState.SystemBaseDir;
        var eventStore = new FileAgentEventStore(ws);

        var tasks = AppState.GetCoordinatedTasks().ToList();
        var sessionId = AppState.SessionId;

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

    public Task<List<AgentSessionRecordDto>> GetSessions() => _controlPlane.GetSessions();

    public Task<List<ReplayEventDto>> GetSessionEvents(string sessionId) => _controlPlane.GetSessionEvents(sessionId);

    public Task<UsageReadModelDto> GetUsage(string sessionId) => _controlPlane.GetUsage(sessionId);

    public Task<SkillControlPlaneState> GetSkills() => _controlPlane.GetSkills();

    public Task<ProviderControlPlaneState> GetProviders() => _controlPlane.GetProviders();

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
        if (!IdempotentApprovalEngine.TryRegisterDecision(requestId, approved, reason, out var errorMsg))
        {
            if (errorMsg != null)
            {
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
