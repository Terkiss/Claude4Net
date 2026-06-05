using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// Client wrapper for interacting with an MCP (Model Context Protocol) server.
    /// It can list tools exposed by the server and invoke them directly.
    /// </summary>
    public class McpClient
    {
        private readonly McpStdioTransport _transport;

        /// <summary>
        /// Initializes a new MCP client with the provided transport.
        /// </summary>
        /// <param name="transport">The transport used to communicate with the MCP server.</param>
        public McpClient(McpStdioTransport transport)
        {
            _transport = transport;
        }

        /// <summary>
        /// Retrieves the list of tools currently available from the MCP server.
        /// </summary>
        /// <returns>A list of MCP tools returned by the server.</returns>
        public async Task<List<McpTool>> ListToolsAsync()
        {
            var response = await _transport.SendRequestAsync("tools/list", null);
            if (response.Result.HasValue)
            {
                var tools = response.Result.Value.GetProperty("tools").Deserialize<List<McpTool>>();
                return tools ?? new List<McpTool>();
            }

            return new List<McpTool>();
        }

        /// <summary>
        /// Requests execution of a specific tool on the MCP server.
        /// </summary>
        /// <param name="name">The name of the tool to invoke.</param>
        /// <param name="arguments">The argument payload passed to the tool.</param>
        /// <returns>The tool execution result returned by the MCP server.</returns>
        public async Task<McpCallToolResult> CallToolAsync(string name, Dictionary<string, object> arguments)
        {
            var response = await _transport.SendRequestAsync("tools/call", new { name, arguments });
            if (response.Result.HasValue)
            {
                return response.Result.Value.Deserialize<McpCallToolResult>() ?? new McpCallToolResult();
            }

            return new McpCallToolResult { IsError = true };
        }
    }
}
