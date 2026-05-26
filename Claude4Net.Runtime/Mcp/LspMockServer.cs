using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.Mcp
{
    /// <summary>
    /// Helper class to create a mock-configured <see cref="LspClient"/> for testing
    /// without starting a real LSP subprocess.
    /// </summary>
    public static class LspMockServer
    {
        public static LspClient CreateMockLspClient(Func<JsonRpcRequest, object> requestHandler)
        {
            if (requestHandler == null) throw new ArgumentNullException(nameof(requestHandler));

            // 1. Create an uninitialized LspClient
            var lspClient = new LspClient();

            // 2. Create an uninitialized LspStdioTransport
            var transport = (LspStdioTransport)RuntimeHelpers.GetUninitializedObject(typeof(LspStdioTransport));

            // 3. Create mock streams
            var inputPipe = new MockPipeStream();
            var outputPipe = new MockPipeStream();

            // Set up the reader/writer on the transport
            var writer = new StreamWriter(inputPipe, new UTF8Encoding(false)) { AutoFlush = true };

            // Set the private fields of LspStdioTransport using reflection
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;

            var writerField = typeof(LspStdioTransport).GetField("_writer", flags);
            if (writerField != null) writerField.SetValue(transport, writer);

            var streamField = typeof(LspStdioTransport).GetField("_readerStream", flags);
            if (streamField != null) streamField.SetValue(transport, outputPipe);

            var idField = typeof(LspStdioTransport).GetField("_requestId", flags);
            if (idField != null) idField.SetValue(transport, 0);

            // Set the private field _transport of LspClient using reflection
            var transportField = typeof(LspClient).GetField("_transport", flags);
            if (transportField != null) transportField.SetValue(lspClient, transport);

            // Set IsInitialized backing field
            var backingField = typeof(LspClient).GetField("<IsInitialized>k__BackingField", flags);
            if (backingField != null)
            {
                backingField.SetValue(lspClient, true);
            }
            else
            {
                var prop = typeof(LspClient).GetProperty("IsInitialized");
                prop?.GetSetMethod(true)?.Invoke(lspClient, new object[] { true });
            }

            // Start a background task to process requests
            Task.Run(async () =>
            {
                string logPath = @"C:\Users\dl200\.gemini\antigravity\brain\1ddcbc6e-fbb7-4a36-997c-cf1557e4f885\scratch\lsp_debug.txt";
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath)!);
                    System.IO.File.WriteAllText(logPath, $"[LOG] Background task started\n");
                    while (true)
                    {
                        int contentLength = -1;
                        string? line;
                        System.IO.File.AppendAllText(logPath, $"[LOG] Waiting for headers...\n");
                        while (!string.IsNullOrEmpty(line = await ReadLineAsync(inputPipe)))
                        {
                            System.IO.File.AppendAllText(logPath, $"[LOG] Header line: '{line}'\n");
                            line = line.Trim('\uFEFF');
                            if (line.StartsWith("Content-Length:"))
                            {
                                contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                            }
                        }

                        System.IO.File.AppendAllText(logPath, $"[LOG] Parsed Content-Length: {contentLength}\n");
                        if (contentLength == -1) break;

                        byte[] buffer = new byte[contentLength];
                        int totalRead = 0;
                        while (totalRead < contentLength)
                        {
                            int read = await inputPipe.ReadAsync(buffer, totalRead, contentLength - totalRead);
                            System.IO.File.AppendAllText(logPath, $"[LOG] Read {read} bytes of JSON\n");
                            if (read == 0) break;
                            totalRead += read;
                        }

                        string jsonRequest = Encoding.UTF8.GetString(buffer);
                        System.IO.File.AppendAllText(logPath, $"[LOG] Deserializing JSON: '{jsonRequest}'\n");
                        var request = JsonSerializer.Deserialize<JsonRpcRequest>(jsonRequest);
                        if (request != null)
                        {
                            System.IO.File.AppendAllText(logPath, $"[LOG] Request Method: {request.Method}, Id: {request.Id}\n");
                            object responseObj = requestHandler(request);
                            string serializedResp = JsonSerializer.Serialize(responseObj);
                            System.IO.File.AppendAllText(logPath, $"[LOG] Handler response serialized: '{serializedResp}'\n");
                            byte[] responseBytes = Encoding.UTF8.GetBytes(serializedResp);

                            string responseHeader = $"Content-Length: {responseBytes.Length}\r\n\r\n";
                            byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeader);

                            System.IO.File.AppendAllText(logPath, $"[LOG] Writing response header and bytes...\n");
                            await outputPipe.WriteAsync(headerBytes, 0, headerBytes.Length);
                            await outputPipe.WriteAsync(responseBytes, 0, responseBytes.Length);
                            await outputPipe.FlushAsync();
                            System.IO.File.AppendAllText(logPath, $"[LOG] Response written and flushed\n");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(logPath, $"[EXCEPTION] {ex}\n");
                    Console.WriteLine("MOCK LSP EXCEPTION: " + ex.ToString());
                }
            });

            return lspClient;
        }

        private static async Task<string?> ReadLineAsync(Stream stream)
        {
            var lineBytes = new List<byte>();
            int b;
            while (true)
            {
                b = stream.ReadByte();
                if (b == -1) break;
                if (b == '\n') break;
                if (b != '\r') lineBytes.Add((byte)b);
            }
            if (b == -1 && lineBytes.Count == 0) return null;
            return Encoding.UTF8.GetString(lineBytes.ToArray());
        }
    }

    /// <summary>
    /// Thread-safe in-memory stream for pipe-like read/write interactions.
    /// </summary>
    public class MockPipeStream : Stream
    {
        private readonly BlockingCollection<byte> _queue = new();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int read = 0;
            while (read < count)
            {
                if (_queue.TryTake(out byte b))
                {
                    buffer[offset + read] = b;
                    read++;
                }
                else if (read > 0)
                {
                    break;
                }
                else
                {
                    try
                    {
                        buffer[offset + read] = _queue.Take();
                        read++;
                    }
                    catch (InvalidOperationException)
                    {
                        // Queue completed/disposed
                        break;
                    }
                }
            }
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                return new ValueTask<int>(Read(array.Array!, array.Offset, array.Count));
            }
            byte[] temp = new byte[buffer.Length];
            int read = Read(temp, 0, temp.Length);
            temp.CopyTo(buffer);
            return new ValueTask<int>(read);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            for (int i = 0; i < count; i++)
            {
                _queue.Add(buffer[offset + i]);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            if (System.Runtime.InteropServices.MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
            {
                Write(array.Array!, array.Offset, array.Count);
            }
            else
            {
                Write(buffer.ToArray(), 0, buffer.Length);
            }
            return ValueTask.CompletedTask;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _queue.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
