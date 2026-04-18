using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class LspToolInput
    {
        public string operation { get; set; } = string.Empty;
        public string filePath { get; set; } = string.Empty;
        public int line { get; set; }
        public int character { get; set; }
        public string query { get; set; } = string.Empty; // For workspaceSymbol
    }

    public class LspTool : ITool
    {
        private readonly LspClient _lspClient;

        public LspTool(LspClient lspClient)
        {
            _lspClient = lspClient;
        }

        public string Name => "LspTool";
        public string Description => "Language Server Protocol (LSP) tool for code intelligence. Use it to find definitions, references, hover information, and document/workspace symbols.";
        public List<string>? Aliases => new() { "lsp", "gotoDef", "findRefs" };

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                operation = new
                {
                    type = "string",
                    description = "The LSP operation to perform: 'goToDefinition', 'findReferences', 'hover', 'documentSymbol', 'workspaceSymbol'"
                },
                filePath = new
                {
                    type = "string",
                    description = "The absolute or relative path to the file (required for all operations except workspaceSymbol)"
                },
                line = new
                {
                    type = "integer",
                    description = "The 0-based line number (required for goToDefinition, findReferences, hover)"
                },
                character = new
                {
                    type = "integer",
                    description = "The 0-based character offset (required for goToDefinition, findReferences, hover)"
                },
                query = new
                {
                    type = "string",
                    description = "The search query (required for workspaceSymbol)"
                }
            },
            required = new[] { "operation" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<LspToolInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments for LspTool");

            string uri = "";
            if (!string.IsNullOrEmpty(input.filePath))
            {
                var fullPath = Path.GetFullPath(input.filePath);
                uri = new Uri(fullPath).AbsoluteUri;
            }

            switch (input.operation)
            {
                case "goToDefinition":
                    var defs = await _lspClient.GoToDefinitionAsync(uri, input.line, input.character);
                    return new { operation = input.operation, result = defs, resultCount = defs.Count };
                
                case "findReferences":
                    var refs = await _lspClient.FindReferencesAsync(uri, input.line, input.character);
                    return new { operation = input.operation, result = refs, resultCount = refs.Count };
                
                case "hover":
                    var hover = await _lspClient.HoverAsync(uri, input.line, input.character);
                    return new { operation = input.operation, result = hover };
                
                case "documentSymbol":
                    var docSymbols = await _lspClient.DocumentSymbolAsync(uri);
                    return new { operation = input.operation, result = docSymbols, resultCount = docSymbols.Count };
                
                case "workspaceSymbol":
                    var workspaceSymbols = await _lspClient.WorkspaceSymbolAsync(input.query);
                    return new { operation = input.operation, result = workspaceSymbols, resultCount = workspaceSymbols.Count };
                
                default:
                    throw new ArgumentException($"Unsupported LSP operation: {input.operation}");
            }
        }
    }
}
