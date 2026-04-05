using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class FileWriteInput
    {
        public string file_path { get; set; } = string.Empty;
        public string content { get; set; } = string.Empty;
    }

    public class FileWriteTool : ITool
    {
        public string Name => "FileWriteTool";
        public string Description => "Write content to a file.";
        public List<string>? Aliases => new() { "write" };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<FileWriteInput>(arguments, options) 
                        ?? throw new ArgumentException("Invalid arguments");

            string? dir = Path.GetDirectoryName(input.file_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(input.file_path, input.content);
            return new { filePath = input.file_path, status = "Success" };
        }
    }
}
