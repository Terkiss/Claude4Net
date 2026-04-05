using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    public class LspClient : IDisposable
    {
        private LspStdioTransport? _transport;
        private readonly object _lock = new();
        public bool IsInitialized { get; private set; }

        public LspClient()
        {
        }

        public async Task StartAsync(string? command = null, string[]? args = null)
        {
            if (IsInitialized) return;

            // Default to csharp-ls if not specified
            command ??= "csharp-ls";
            args ??= Array.Empty<string>();

            // Ensure tool is installed if using default
            if (command == "csharp-ls")
            {
                await EnsureCSharpLsInstalledAsync();
            }

            lock (_lock)
            {
                if (_transport != null) return;
                _transport = new LspStdioTransport(command, args);
            }

            // Perform LSP Initialization Handshake
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

            var response = await _transport.SendRequestAsync("initialize", initializeParams);
            if (response.Error != null)
            {
                throw new Exception($"LSP Initialization failed: {response.Error.Value.GetRawText()}");
            }

            // Send initialized notification (LSP spec)
            // LspStdioTransport.SendRequestAsync sends it with an ID. 
            // In a real LSP client, we'd have a SendNotification method.
            // For now, we'll just send it as a request to satisfy the server.
            try { await _transport.SendRequestAsync("initialized", new { }); } catch { }
            
            IsInitialized = true;
        }

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

        public async Task<List<LspDocumentSymbol>> DocumentSymbolAsync(string uri)
        {
            var @params = new { textDocument = new { uri } };
            var result = await SendRequestAsync("textDocument/documentSymbol", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspDocumentSymbol>();
            return result.Value.Deserialize<List<LspDocumentSymbol>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspDocumentSymbol>();
        }

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
