using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class FileReadInput
    {
        public string file_path { get; set; } = string.Empty;
        public int? offset { get; set; }
        public int? limit { get; set; }

        public string path { get => file_path; set => file_path = value; }
    }

    public class FileReadTool : ITool
    {
        public string Name => "FileReadTool";
        public string Description => "Read the content of a file.";
        public List<string>? Aliases => new() { "read" };
        public object? InputSchema => new { type = "object", properties = new { file_path = new { type = "string", description = "Path to read" } }, required = new[] { "file_path" } };
        public bool IsConcurrencySafe => true;

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileReadInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            if (!File.Exists(input.file_path)) throw new FileNotFoundException($"File not found: {input.file_path}");

            var allLines = await File.ReadAllLinesAsync(input.file_path);
            int startLine = input.offset ?? 1;
            int lineCount = input.limit ?? (allLines.Length - startLine + 1);

            var selectedLines = allLines.Skip(Math.Max(0, startLine - 1)).Take(Math.Max(0, lineCount)).ToList();
            return new { filePath = input.file_path, content = string.Join("\n", selectedLines), totalLines = allLines.Length };
        }
    }
}
