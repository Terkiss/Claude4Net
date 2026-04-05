using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    public class BashInput
    {
        public string command { get; set; } = string.Empty;
        public bool? restart { get; set; }
    }

    public class BashTool : ITool
    {
        public string Name => "BashTool";
        public string Description => "Execute a shell command in the local system.";
        public List<string>? Aliases => new() { "bash", "sh", "shell" };
        public object? InputSchema => new { type = "object", properties = new { command = new { type = "string", description = "The shell command to run" } }, required = new[] { "command" } };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<BashInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments for BashTool");

            using var process = new Process();
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $"-NoProfile -Command \"{input.command}\"";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return new { command = input.command, output = output, error = error, exitCode = process.ExitCode };
        }
    }
}
