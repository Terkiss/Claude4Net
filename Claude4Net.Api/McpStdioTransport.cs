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
    /// 표준 입출력을 통해 MCP 서버와 JSON RPC 메시지를 교환하는 전송 계층입니다.
    /// </summary>
    public class McpStdioTransport : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly StreamReader _reader;
        private int _requestId = 0;

        /// <summary>
        /// McpStdioTransport의 새 인스턴스를 초기화하고 MCP 서버 프로세스를 실행합니다.
        /// </summary>
        /// <param name="command">실행할 명령어</param>
        /// <param name="args">명령어 인자</param>
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
        /// JSON RPC 요청을 서버로 전송하고 즉시 응답 한 줄을 읽어 반환합니다.
        /// </summary>
        /// <param name="method">RPC 메서드</param>
        /// <param name="params">매개변수</param>
        /// <returns>JSON RPC 응답</returns>
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
