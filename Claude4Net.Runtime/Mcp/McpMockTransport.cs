using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Mcp
{
    /// <summary>
    /// Mock transport implementation of <see cref="IMcpTransport"/> for local testing.
    /// Handles requests and generates replies in memory without starting subprocesses or network socket connections.
    /// </summary>
    public class McpMockTransport : IMcpTransport
    {
        private readonly ConcurrentQueue<string> _inbox = new();
        private readonly SemaphoreSlim _inboxSignal = new(0);
        private readonly Dictionary<string, Func<JsonElement, object>> _mockHandlers = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a handler for a specific JSON-RPC method.
        /// </summary>
        public void RegisterHandler(string method, Func<JsonElement, object> handler)
        {
            if (string.IsNullOrEmpty(method)) throw new ArgumentNullException(nameof(method));
            _mockHandlers[method] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task SendMessageAsync(string message, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(message)) return;

            try
            {
                using var doc = JsonDocument.Parse(message);
                var root = doc.RootElement;
                if (root.TryGetProperty("method", out var methodProp))
                {
                    string method = methodProp.GetString() ?? string.Empty;
                    object? id = null;
                    if (root.TryGetProperty("id", out var idProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.Number) id = idProp.GetInt32();
                        else if (idProp.ValueKind == JsonValueKind.String) id = idProp.GetString();
                    }

                    root.TryGetProperty("params", out var paramsProp);

                    object? result = null;
                    object? error = null;

                    if (_mockHandlers.TryGetValue(method, out var handler))
                    {
                        try
                        {
                            result = handler(paramsProp);
                        }
                        catch (Exception ex)
                        {
                            error = new JsonRpcError { Code = -32603, Message = ex.Message };
                        }
                    }
                    else
                    {
                        error = new JsonRpcError { Code = -32601, Message = $"Method '{method}' not found." };
                    }

                    var response = new JsonRpcResponse
                    {
                        Id = id,
                        Result = result != null ? JsonSerializer.SerializeToElement(result) : null,
                        Error = error != null ? JsonSerializer.SerializeToElement(error) : null
                    };

                    string jsonResponse = JsonSerializer.Serialize(response);
                    _inbox.Enqueue(jsonResponse);
                    _inboxSignal.Release();
                }
            }
            catch (Exception ex)
            {
                var errorResponse = new JsonRpcResponse
                {
                    Error = JsonSerializer.SerializeToElement(new JsonRpcError { Code = -32700, Message = ex.Message })
                };
                _inbox.Enqueue(JsonSerializer.Serialize(errorResponse));
                _inboxSignal.Release();
            }

            await Task.CompletedTask;
        }

        public async Task<string?> ReceiveMessageAsync(CancellationToken ct = default)
        {
            await _inboxSignal.WaitAsync(ct);
            if (_inbox.TryDequeue(out string? message))
            {
                return message;
            }
            return null;
        }

        public void Dispose()
        {
            _inboxSignal.Dispose();
        }
    }
}
