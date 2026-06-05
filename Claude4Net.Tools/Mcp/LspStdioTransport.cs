using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    /// <summary>
    /// Transport layer that communicates with an LSP server via standard I/O (stdin/stdout) streams.
    /// Implements the JSON-RPC message framing protocol with Content-Length headers as defined by LSP.
    /// </summary>
    public class LspStdioTransport : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly Stream _readerStream;
        private int _requestId = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="LspStdioTransport"/> class and starts the LSP server process.
        /// </summary>
        /// <param name="command">The LSP server executable file path.</param>
        /// <param name="args">Command-line arguments for the server process.</param>
        public LspStdioTransport(string command, string[] args)
        {
            _process = new Process();
            _process.StartInfo.FileName = command;
            foreach (var arg in args) _process.StartInfo.ArgumentList.Add(arg);
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.CreateNoWindow = true;
            
            // csharp-ls may require specific shell execution settings depending on the environment
            if (command == "csharp-ls") {
                _process.StartInfo.UseShellExecute = false; 
            }

            _process.Start();

            _writer = _process.StandardInput;
            _readerStream = _process.StandardOutput.BaseStream;

            _process.ErrorDataReceived += (s, e) => {
                if (!string.IsNullOrEmpty(e.Data)) Console.WriteLine($"LSP Error Log: {e.Data}");
            };
            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// Sends a JSON-RPC request to the LSP server and waits for the response.
        /// Uses Content-Length header framing for the outgoing message.
        /// </summary>
        /// <param name="method">The RPC method name to invoke.</param>
        /// <param name="params">The request parameters.</param>
        /// <returns>The JSON-RPC response from the server.</returns>
        public async Task<JsonRpcResponse> SendRequestAsync(string method, object? @params)
        {
            var id = ++_requestId;
            var request = new JsonRpcRequest { Id = id, Method = method, Params = @params };
            string json = JsonSerializer.Serialize(request);
            
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            string header = $"Content-Length: {jsonBytes.Length}\r\n\r\n";
            
            await _writer.WriteAsync(header);
            await _writer.FlushAsync();
            await _writer.BaseStream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            await _writer.BaseStream.FlushAsync();

            return await ReadResponseAsync();
        }

        /// <summary>
        /// Reads and parses a complete LSP response packet from the server's stdout stream.
        /// Handles Content-Length header parsing and body extraction per the LSP specification.
        /// </summary>
        private async Task<JsonRpcResponse> ReadResponseAsync()
        {
            while (true)
            {
                int contentLength = -1;
                string? line;
                // Parse header section to extract Content-Length value
                while (!string.IsNullOrEmpty(line = await ReadLineAsync(_readerStream)))
                {
                    if (line.StartsWith("Content-Length:"))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                if (contentLength == -1) throw new Exception("No Content-Length header found.");

                // Read the message body based on the declared content length
                byte[] buffer = new byte[contentLength];
                int totalRead = 0;
                while (totalRead < contentLength)
                {
                    int read = await _readerStream.ReadAsync(buffer, totalRead, contentLength - totalRead);
                    if (read == 0) throw new Exception("LSP server closed connection.");
                    totalRead += read;
                }

                string json = Encoding.UTF8.GetString(buffer);
                var response = JsonSerializer.Deserialize<JsonRpcResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                // If the response has an ID, it is a reply to our request (simplified matching)
                if (response != null && response.Id != null) return response;
                
                // Skip notifications (messages without an ID) and continue reading
            }
        }

        /// <summary>
        /// Reads a single line from the stream, handling both \r\n and \n line endings.
        /// </summary>
        private async Task<string?> ReadLineAsync(Stream stream)
        {
            List<byte> lineBytes = new List<byte>();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b != '\r') lineBytes.Add((byte)b);
            }
            if (b == -1 && lineBytes.Count == 0) return null;
            return Encoding.UTF8.GetString(lineBytes.ToArray());
        }

        public void Dispose()
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
        }
    }
}
