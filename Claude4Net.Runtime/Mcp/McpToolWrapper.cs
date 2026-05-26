using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Mcp
{
    /// <summary>
    /// Wrapper that adapts an MCP tool to the <see cref="ITool"/> interface.
    /// </summary>
    public class McpToolWrapper : ITool
    {
        private readonly McpRuntimeClient _client;
        private readonly McpTool _mcpTool;

        public McpToolWrapper(McpRuntimeClient client, McpTool mcpTool)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _mcpTool = mcpTool ?? throw new ArgumentNullException(nameof(mcpTool));
        }

        public string Name => _mcpTool.Name ?? string.Empty;
        public string Description => _mcpTool.Description ?? string.Empty;
        public IEnumerable<string>? Aliases => null;
        public object? InputSchema => _mcpTool.InputSchema;
        public bool IsConcurrencySafe => false;

        public async Task<object> ExecuteAsync(string arguments, object context, CancellationToken ct = default)
        {
            var argsDict = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(arguments))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments);
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            if (kvp.Value is JsonElement element)
                            {
                                argsDict[kvp.Key] = GetValueFromElement(element);
                            }
                            else
                            {
                                argsDict[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore deserialization error and proceed with empty dictionary
                }
            }

            var result = await _client.CallToolAsync(Name, argsDict, ct);
            if (result.IsError)
            {
                var errors = result.Content != null
                    ? string.Join("\n", result.Content.Select(c => c.Text ?? string.Empty))
                    : "Unknown error";
                throw new Exception($"MCP tool execution failed: {errors}");
            }

            if (result.Content == null || result.Content.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("\n", result.Content.Select(c => c.Text ?? string.Empty));
        }

        private static object GetValueFromElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out long l)) return l;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null!;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                default:
                    return element;
            }
        }
    }
}
