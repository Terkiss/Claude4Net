using Microsoft.AspNetCore.SignalR;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using System.Threading.Tasks;
using System;

namespace Claude4Net.Dashboard.Hubs;

public class ControlPlaneHub : Hub
{
    public Task<string> ExecuteCommand(string commandLine)
    {
        // P1-1 Security Remediation: Command execution via Dashboard SignalR is disabled.
        // Returning explicit deny to prevent unauthorized remote command execution.
        return Task.FromResult("Execution denied: Remote command execution via Dashboard is disabled for security reasons.");
    }
}
