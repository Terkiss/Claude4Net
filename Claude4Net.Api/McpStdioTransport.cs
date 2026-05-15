using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// Transport layer that communicates with an MCP (Model Context Protocol) server via standard I/O streams.
    /// Exchanges JSON-RPC messages using newline-delimited JSON over stdin/stdout.
    /// </summary>
    public class McpStdioTransport : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private int _requestId = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="McpStdioTransport"/> class and starts the MCP server process.
        /// </summary>
        /// <param name="command">The executable command to launch the MCP server.</param>
        /// <param name="args">Command-line arguments for the server process.</param>
        public McpStdioTransport(string command, string[] args)
        {
            _process = new Process();
            _process.StartInfo.FileName = command;
            foreach (var arg in args) _process.StartInfo.ArgumentList.Add(arg);
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            _process.Start();

            _writer = _process.StandardInput;
            _reader = _process.StandardOutput;
        }

        /// <summary>
        /// Sends a JSON-RPC request to the MCP server and reads the immediate single-line response.
        /// </summary>
        /// <param name="method">The RPC method name to invoke.</param>
        /// <param name="params">The request parameters.</param>
        /// <returns>The deserialized JSON-RPC response from the server.</returns>
        public async Task<JsonRpcResponse> SendRequestAsync(string method, object? @params)
        {
            var id = ++_requestId;
            var request = new JsonRpcRequest { Id = id, Method = method, Params = @params };
            string json = JsonSerializer.Serialize(request);
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();

            string? line = await _reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) throw new Exception("MCP server closed connection.");
            return JsonSerializer.Deserialize<JsonRpcResponse>(line) ?? throw new Exception("Invalid response.");
        }

        public void Dispose()
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
        }
    }
}
