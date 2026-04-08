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
    public class LspStdioTransport : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly Stream _readerStream;
        private int _requestId = 0;

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
            
            // On Windows, global tools are often in %USERPROFILE%\.dotnet\tools
            // If command is csharp-ls and not found, we might need full path, but let's try shell first.
            if (command == "csharp-ls") {
                _process.StartInfo.UseShellExecute = true; // To let Windows find it in path
                _process.StartInfo.RedirectStandardInput = true;
                _process.StartInfo.RedirectStandardOutput = true;
                _process.StartInfo.RedirectStandardError = true;
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

        private async Task<JsonRpcResponse> ReadResponseAsync()
        {
            while (true)
            {
                int contentLength = -1;
                string? line;
                while (!string.IsNullOrEmpty(line = await ReadLineAsync(_readerStream)))
                {
                    if (line.StartsWith("Content-Length:"))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                if (contentLength == -1) throw new Exception("No Content-Length header found.");

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
                
                // LSP servers might send notifications or responses to other requests first.
                // For simplicity, we assume the next response is ours, or it's a notification we ignore.
                // Real LSP clients should have a dispatcher.
                if (response != null && response.Id != null) return response;
                
                // If it's a notification (no ID), loop to read next message
            }
        }

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
