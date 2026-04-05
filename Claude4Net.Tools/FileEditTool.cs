using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class FileEditInput
    {
        public string file_path { get; set; } = string.Empty;
        public string old_string { get; set; } = string.Empty;
        public string new_string { get; set; } = string.Empty;
    }

    public class FileEditTool : ITool
    {
        public string Name => "FileEditTool";
        public string Description => "Edit a file.";
        public List<string>? Aliases => new() { "edit" };
        public object? InputSchema => new { type = "object", properties = new { file_path = new { type = "string" }, old_string = new { type = "string" }, new_string = new { type = "string" } }, required = new[] { "file_path", "old_string", "new_string" } };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileEditInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            if (!File.Exists(input.file_path)) throw new FileNotFoundException($"File not found: {input.file_path}");

            string content = await File.ReadAllTextAsync(input.file_path);
            if (!content.Contains(input.old_string)) throw new Exception("String not found.");

            string updated = content.Replace(input.old_string, input.new_string);
            await File.WriteAllTextAsync(input.file_path, updated);

            return new { filePath = input.file_path, status = "Success" };
        }
    }
}
