using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class LsInput 
    { 
        public string path { get; set; } = string.Empty; 
    }

    public class LsTool : ITool
    {
        public string Name => "LsTool";
        public string Description => "List files and directories in a given path.";
        public object? InputSchema => new { type = "object", properties = new { path = new { type = "string", description = "Directory path to list" } }, required = new[] { "path" } };
        public bool IsConcurrencySafe => true;

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<LsInput>(arguments, options) ?? new LsInput();
            
            string targetPath = string.IsNullOrEmpty(input.path) ? Environment.CurrentDirectory : input.path;
            if (!Directory.Exists(targetPath)) throw new DirectoryNotFoundException($"Directory not found: {targetPath}");

            var entries = Directory.GetFileSystemEntries(targetPath)
                .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""))
                .ToList();

            return new { path = targetPath, entries = entries };
        }
    }
}
