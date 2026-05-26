using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Mcp
{
    public class McpRuntimeClient : IDisposable
    {
        private readonly IMcpTransport _transport;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private int _requestIdSequence = 0;
        private CancellationTokenSource? _cts;
        private Task? _receiveLoopTask;

        public McpRuntimeClient(IMcpTransport transport)
        {
            _transport = transport;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            await _transport.StartAsync(ct);
            _cts = new CancellationTokenSource();
            _receiveLoopTask = Task.Run(() => ReceiveLoopAsync(_cts.Token), ct);
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await _transport.ReceiveMessageAsync(ct);
                    if (line == null) break;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("id", out var idProp))
                        {
                            string idStr = idProp.ValueKind == JsonValueKind.Number
                                ? idProp.GetInt32().ToString()
                                : idProp.GetString() ?? "";

                            if (!string.IsNullOrEmpty(idStr) && _pendingRequests.TryRemove(idStr, out var tcs))
                            {
                                tcs.TrySetResult(root.Clone());
                            }
                        }
                    }
                    catch
                    {
                        // Ignore malformed JSON or log it
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        public async Task<JsonElement> SendRequestAsync(string method, object? @params, CancellationToken ct = default)
        {
            int id = Interlocked.Increment(ref _requestIdSequence);
            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = @params
            };

            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id.ToString()] = tcs;

            string json = JsonSerializer.Serialize(request);
            await _transport.SendMessageAsync(json, ct);

            using (ct.Register(() => tcs.TrySetCanceled(ct)))
            {
                return await tcs.Task;
            }
        }

        public async Task<List<McpTool>> ListToolsAsync(CancellationToken ct = default)
        {
            var response = await SendRequestAsync("tools/list", null, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                if (resultProp.TryGetProperty("tools", out var toolsProp) && toolsProp.ValueKind != JsonValueKind.Null)
                {
                    var tools = JsonSerializer.Deserialize<List<McpTool>>(toolsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return tools ?? new List<McpTool>();
                }
            }
            return new List<McpTool>();
        }

        public async Task<McpCallToolResult> CallToolAsync(string name, Dictionary<string, object> arguments, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("tools/call", new { name, arguments }, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                var result = JsonSerializer.Deserialize<McpCallToolResult>(resultProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return result ?? new McpCallToolResult { IsError = true };
            }
            return new McpCallToolResult { IsError = true };
        }

        public async Task<List<McpPrompt>> ListPromptsAsync(CancellationToken ct = default)
        {
            var response = await SendRequestAsync("prompts/list", null, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                if (resultProp.TryGetProperty("prompts", out var promptsProp) && promptsProp.ValueKind != JsonValueKind.Null)
                {
                    var prompts = JsonSerializer.Deserialize<List<McpPrompt>>(promptsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return prompts ?? new List<McpPrompt>();
                }
            }
            return new List<McpPrompt>();
        }

        public async Task<McpGetPromptResult?> GetPromptAsync(string name, Dictionary<string, string> arguments, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("prompts/get", new { name, arguments }, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                return JsonSerializer.Deserialize<McpGetPromptResult>(resultProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return null;
        }

        public async Task<List<McpResource>> ListResourcesAsync(CancellationToken ct = default)
        {
            var response = await SendRequestAsync("resources/list", null, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                if (resultProp.TryGetProperty("resources", out var resProp) && resProp.ValueKind != JsonValueKind.Null)
                {
                    var resources = JsonSerializer.Deserialize<List<McpResource>>(resProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return resources ?? new List<McpResource>();
                }
            }
            return new List<McpResource>();
        }

        public async Task<McpReadResourceResult?> ReadResourceAsync(string uri, CancellationToken ct = default)
        {
            var response = await SendRequestAsync("resources/read", new { uri }, ct);
            if (response.TryGetProperty("result", out var resultProp) && resultProp.ValueKind != JsonValueKind.Null)
            {
                return JsonSerializer.Deserialize<McpReadResourceResult>(resultProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            return null;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _transport.Dispose();
        }
    }

    public class McpPrompt
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<McpPromptArgument>? Arguments { get; set; }
    }

    public class McpPromptArgument
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Required { get; set; }
    }

    public class McpGetPromptResult
    {
        public string Description { get; set; } = string.Empty;
        public List<McpPromptMessage> Messages { get; set; } = new();
    }

    public class McpPromptMessage
    {
        public string Role { get; set; } = string.Empty;
        public McpContent Content { get; set; } = new();
    }

    public class McpResource
    {
        public string Uri { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
    }

    public class McpReadResourceResult
    {
        public List<McpResourceContent> Contents { get; set; } = new();
    }

    public class McpResourceContent
    {
        public string Uri { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string? Text { get; set; }
        public string? Blob { get; set; }
    }
}
