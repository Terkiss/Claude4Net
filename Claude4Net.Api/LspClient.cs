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
    /// LSP(Language Server Protocol) 서버와의 통신을 관리하는 클라이언트 클래스입니다.
    /// 코드 정의 이동, 심볼 조회, 호버 정보 제공 등의 기능을 수행합니다.
    /// </summary>
    public class LspClient : IDisposable
    {
        private LspStdioTransport? _transport;
        private readonly object _lock = new();

        /// <summary>
        /// 클라이언트가 성공적으로 초기화되었는지 여부입니다.
        /// </summary>
        public bool IsInitialized { get; private set; }

        public LspClient()
        {
        }

        /// <summary>
        /// LSP 서버 프로세스를 시작하고 초기화 핸드셰이크를 수행합니다.
        /// </summary>
        /// <param name="command">LSP 서버 실행 명령어 (기본값: csharp-ls)</param>
        /// <param name="args">실행 인자</param>
        public async Task StartAsync(string? command = null, string[]? args = null)
        {
            if (IsInitialized) return;

            command ??= "csharp-ls";
            args ??= Array.Empty<string>();

            // csharp-ls 사용 시 설치 여부 확인 및 자동 설치 시도
            if (command == "csharp-ls")
            {
                await EnsureCSharpLsInstalledAsync();
            }

            lock (_lock)
            {
                if (_transport != null) return;
                _transport = new LspStdioTransport(command, args);
            }

            // LSP 초기화 매개변수 구성
            var rootPath = Directory.GetCurrentDirectory();
            var initializeParams = new
            {
                processId = Environment.ProcessId,
                rootPath = rootPath,
                rootUri = new Uri(rootPath).AbsoluteUri,
                capabilities = new 
                {
                    textDocument = new
                    {
                        definition = new { dynamicRegistration = true },
                        references = new { dynamicRegistration = true },
                        hover = new { contentFormat = new[] { "markdown", "plaintext" } },
                        documentSymbol = new { hierarchicalDocumentSymbolSupport = true }
                    },
                    workspace = new
                    {
                        symbol = new { dynamicRegistration = true }
                    }
                },
                initializationOptions = new { }
            };

            // initialize 요청 전송
            var response = await _transport.SendRequestAsync("initialize", initializeParams);
            if (response.Error != null)
            {
                throw new Exception($"LSP Initialization failed: {response.Error.Value.GetRawText()}");
            }

            // initialized 알림 전송 (LSP 규격)
            try { await _transport.SendRequestAsync("initialized", new { }); } catch { }
            
            IsInitialized = true;
        }

        /// <summary>
        /// .NET 도구 중 csharp-ls가 설치되어 있는지 확인하고 없으면 설치합니다.
        /// </summary>
        private async Task EnsureCSharpLsInstalledAsync()
        {
            bool installed = false;
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "csharp-ls",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                await process.WaitForExitAsync();
                if (process.ExitCode == 0) installed = true;
            }
            catch { }

            if (!installed)
            {
                Console.WriteLine("LSP: csharp-ls not found. Attempting installation...");
                var installProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "tool install -g csharp-ls",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                installProcess.Start();
                await installProcess.WaitForExitAsync();
            }
        }

        /// <summary>
        /// LSP 서버에 JSON RPC 요청을 전송하고 결과를 반환받습니다.
        /// </summary>
        /// <param name="method">LSP 메서드명</param>
        /// <param name="params">매개변수 객체</param>
        /// <returns>응답 결과 JSON</returns>
        public async Task<JsonElement?> SendRequestAsync(string method, object? @params)
        {
            if (!IsInitialized)
            {
                await StartAsync();
            }

            if (_transport == null) throw new InvalidOperationException("LSP Transport failed to start.");
            
            var response = await _transport.SendRequestAsync(method, @params);
            if (response.Error != null)
            {
                throw new Exception($"LSP Request '{method}' failed: {response.Error.Value.GetRawText()}");
            }
            return response.Result;
        }

        /// <summary>
        /// 특정 위치의 코드 정의(Definition)로 이동하기 위한 정보를 가져옵니다.
        /// </summary>
        public async Task<List<LspLocation>> GoToDefinitionAsync(string uri, int line, int character)
        {
            var @params = new
            {
                textDocument = new { uri },
                position = new { line, character }
            };
            var result = await SendRequestAsync("textDocument/definition", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspLocation>();
            
            if (result.Value.ValueKind == JsonValueKind.Array)
            {
                return result.Value.Deserialize<List<LspLocation>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspLocation>();
            }
            else
            {
                var loc = result.Value.Deserialize<LspLocation>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return loc != null ? new List<LspLocation> { loc } : new List<LspLocation>();
            }
        }

        /// <summary>
        /// 특정 심볼의 참조(References) 위치 목록을 검색합니다.
        /// </summary>
        public async Task<List<LspLocation>> FindReferencesAsync(string uri, int line, int character)
        {
            var @params = new
            {
                textDocument = new { uri },
                position = new { line, character },
                context = new { includeDeclaration = true }
            };
            var result = await SendRequestAsync("textDocument/references", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspLocation>();
            return result.Value.Deserialize<List<LspLocation>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspLocation>();
        }

        /// <summary>
        /// 특정 위치의 호버(Hover) 정보(툴팁 내용 등)를 가져옵니다.
        /// </summary>
        public async Task<LspHover?> HoverAsync(string uri, int line, int character)
        {
            var @params = new
            {
                textDocument = new { uri },
                position = new { line, character }
            };
            var result = await SendRequestAsync("textDocument/hover", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return null;
            return result.Value.Deserialize<LspHover>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        /// <summary>
        /// 문서 내의 심볼(클래스, 메서드 등) 구조 정보를 가져옵니다.
        /// </summary>
        public async Task<List<LspDocumentSymbol>> DocumentSymbolAsync(string uri)
        {
            var @params = new { textDocument = new { uri } };
            var result = await SendRequestAsync("textDocument/documentSymbol", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspDocumentSymbol>();
            return result.Value.Deserialize<List<LspDocumentSymbol>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspDocumentSymbol>();
        }

        /// <summary>
        /// 워크스페이스 전체에서 심볼을 검색합니다.
        /// </summary>
        public async Task<List<LspSymbolInformation>> WorkspaceSymbolAsync(string query)
        {
            var @params = new { query };
            var result = await SendRequestAsync("workspace/symbol", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspSymbolInformation>();
            return result.Value.Deserialize<List<LspSymbolInformation>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspSymbolInformation>();
        }

        public void Dispose()
        {
            _transport?.Dispose();
        }
    }
}
