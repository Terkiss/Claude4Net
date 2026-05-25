using System;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.Runtime.Mcp
{
    /// <summary>
    /// Transport interface for Model Context Protocol (MCP) communication.
    /// </summary>
    public interface IMcpTransport : IDisposable
    {
        /// <summary>
        /// Starts the transport connection/process.
        /// </summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>
        /// Sends a raw string message.
        /// </summary>
        Task SendMessageAsync(string message, CancellationToken ct = default);

        /// <summary>
        /// Receives a raw string message. Returns null if the transport is closed.
        /// </summary>
        Task<string?> ReceiveMessageAsync(CancellationToken ct = default);
    }
}
