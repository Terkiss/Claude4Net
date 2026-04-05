using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    public class McpClient
    {
        private readonly McpStdioTransport _transport;

        public McpClient(McpStdioTransport transport)
        {
            _transport = transport;
        }

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
