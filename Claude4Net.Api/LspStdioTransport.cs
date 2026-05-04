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
    /// 표준 입출력(Stdio) 스트림을 사용하여 LSP 서버와 통신하는 저수준 전송 계층입니다.
    /// JSON RPC 기반의 메시지 프레이밍(Content-Length 헤더 처리)을 담당합니다.
    /// </summary>
    public class LspStdioTransport : IDisposable
    {
        private readonly Process _process;
        private readonly StreamWriter _writer;
        private readonly Stream _readerStream;
        private int _requestId = 0;

        /// <summary>
        /// LspStdioTransport의 새 인스턴스를 초기화하고 서버 프로세스를 시작합니다.
        /// </summary>
        /// <param name="command">LSP 서버 실행 파일 경로</param>
        /// <param name="args">실행 인자 목록</param>
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
            
            // csharp-ls의 경우 환경에 따라 쉘 실행이 필요할 수 있음
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
        /// LSP 서버에 요청을 전송하고 응답을 기다립니다.
        /// </summary>
        /// <param name="method">RPC 메서드명</param>
        /// <param name="params">매개변수</param>
        /// <returns>JSON RPC 응답</returns>
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
        /// 스트림으로부터 LSP 규격의 응답 패킷을 읽어 파싱합니다.
        /// </summary>
        private async Task<JsonRpcResponse> ReadResponseAsync()
        {
            while (true)
            {
                int contentLength = -1;
                string? line;
                // 헤더 영역 읽기 (Content-Length 파싱)
                while (!string.IsNullOrEmpty(line = await ReadLineAsync(_readerStream)))
                {
                    if (line.StartsWith("Content-Length:"))
                    {
                        contentLength = int.Parse(line.Substring(15).Trim());
                    }
                }

                if (contentLength == -1) throw new Exception("No Content-Length header found.");

                // 데이터 본문 읽기
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
                
                // ID가 있는 경우 해당 요청에 대한 응답으로 간주 (단순 구현)
                if (response != null && response.Id != null) return response;
                
                // 알림(Notification)인 경우 무시하고 다음 메시지 대기
            }
        }

        /// <summary>
        /// 스트림에서 한 줄을 읽습니다 (\r\n 또는 \n 처리).
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
