using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Mcp
{
    /// <summary>
    /// Service registry for dynamically register and manage MCP clients and their tools.
    /// </summary>
    public class McpRegistry : IDisposable
    {
        private readonly List<McpRuntimeClient> _clients = new();
        private readonly ConcurrentDictionary<string, McpToolWrapper> _registeredTools = new();

        /// <summary>
        /// Registers a new MCP server connection via the specified transport,
        /// discovers its tools, and dynamically registers them into the ToolOrchestrator.
        /// </summary>
        public async Task RegisterServerAsync(IMcpTransport transport, ToolOrchestrator orchestrator, CancellationToken ct = default)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));
            if (orchestrator == null) throw new ArgumentNullException(nameof(orchestrator));

            var client = new McpRuntimeClient(transport);
            await client.StartAsync(ct);

            var tools = await client.ListToolsAsync(ct);
            foreach (var tool in tools)
            {
                if (string.IsNullOrEmpty(tool.Name)) continue;

                var wrapper = new McpToolWrapper(client, tool);
                orchestrator.AddTool(wrapper);
                _registeredTools[tool.Name] = wrapper;
            }

            lock (_clients)
            {
                _clients.Add(client);
            }
        }

        /// <summary>
        /// Gets a dictionary of all dynamically registered MCP tools by name.
        /// </summary>
        public IReadOnlyDictionary<string, McpToolWrapper> GetRegisteredTools() => _registeredTools;

        public void Dispose()
        {
            lock (_clients)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }
                _clients.Clear();
            }
            _registeredTools.Clear();
        }
    }
}
