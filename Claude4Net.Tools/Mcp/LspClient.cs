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
    /// Client wrapper for interacting with a Language Server Protocol (LSP) server.
    /// Provides high-level methods for code navigation, symbol lookup, hover info, and definition jumping.
    /// </summary>
    public class LspClient : IDisposable
    {
        private LspStdioTransport? _transport;
        private readonly object _lock = new();

        /// <summary>
        /// Gets whether the LSP client has been successfully initialized.
        /// </summary>
        public bool IsInitialized { get; private set; }

        public LspClient()
        {
        }

        /// <summary>
        /// Starts the LSP server process and performs the initialization handshake.
        /// Configures client capabilities for text document and workspace operations.
        /// </summary>
        /// <param name="command">The LSP server executable name (defaults to csharp-ls).</param>
        /// <param name="args">Optional command-line arguments for the server.</param>
        public async Task StartAsync(string? command = null, string[]? args = null)
        {
            if (IsInitialized) return;

            command ??= "csharp-ls";
            args ??= Array.Empty<string>();

            // Ensure csharp-ls is installed before attempting to start
            if (command == "csharp-ls")
            {
                await EnsureCSharpLsInstalledAsync();
            }

            lock (_lock)
            {
                if (_transport != null) return;
                _transport = new LspStdioTransport(command, args);
            }

            // Configure LSP initialization parameters with client capabilities
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

            // Send the initialize request to the LSP server
            var response = await _transport.SendRequestAsync("initialize", initializeParams);
            if (response.Error != null)
            {
                throw new Exception($"LSP Initialization failed: {response.Error.Value.GetRawText()}");
            }

            // Send the initialized notification as required by the LSP specification
            try { await _transport.SendRequestAsync("initialized", new { }); } catch { }
            
            IsInitialized = true;
        }

        /// <summary>
        /// Checks whether the csharp-ls .NET tool is installed, and installs it if not found.
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
        /// Sends a JSON-RPC request to the LSP server and returns the result.
        /// Automatically starts the server if it has not been initialized.
        /// </summary>
        /// <param name="method">The LSP method name to invoke.</param>
        /// <param name="params">The request parameters object.</param>
        /// <returns>The JSON result from the server response, or null if no result.</returns>
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
        /// Navigates to the definition of the symbol at the specified document position.
        /// </summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="line">The zero-based line number.</param>
        /// <param name="character">The zero-based character offset within the line.</param>
        /// <returns>A list of location results pointing to the definition sites.</returns>
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
        /// Finds all references to the symbol at the specified document position.
        /// </summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="line">The zero-based line number.</param>
        /// <param name="character">The zero-based character offset within the line.</param>
        /// <returns>A list of locations where the symbol is referenced.</returns>
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
        /// Retrieves hover information (e.g., type signatures, documentation) for the symbol at the specified position.
        /// </summary>
        /// <param name="uri">The document URI.</param>
        /// <param name="line">The zero-based line number.</param>
        /// <param name="character">The zero-based character offset within the line.</param>
        /// <returns>The hover information, or null if no hover data is available.</returns>
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
        /// Retrieves the document symbol hierarchy (classes, methods, properties, etc.) for a given document.
        /// </summary>
        /// <param name="uri">The document URI to query symbols for.</param>
        /// <returns>A hierarchical list of document symbols.</returns>
        public async Task<List<LspDocumentSymbol>> DocumentSymbolAsync(string uri)
        {
            var @params = new { textDocument = new { uri } };
            var result = await SendRequestAsync("textDocument/documentSymbol", @params);
            
            if (result == null || result.Value.ValueKind == JsonValueKind.Null) return new List<LspDocumentSymbol>();
            return result.Value.Deserialize<List<LspDocumentSymbol>>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<LspDocumentSymbol>();
        }

        /// <summary>
        /// Searches for symbols matching the given query across the entire workspace.
        /// </summary>
        /// <param name="query">The search query string to match against symbol names.</param>
        /// <returns>A list of matching symbol information from the workspace.</returns>
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
