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
            
            // Task.WhenAll을 통한 병렬 스트림 읽기 (Deadlock 방지)
            var outTask = process.StandardOutput.ReadToEndAsync();
            var errTask = process.StandardError.ReadToEndAsync();
            
            try
            {
                // 타임아웃 60초 설정 (대화형 프롬프트 등으로 인한 무한 대기 방지)
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
                await process.WaitForExitAsync(cts.Token);
            }
            catch (TaskCanceledException)
            {
                process.Kill(true);
                return new { command = input.command, output = "", error = "Command execution timed out after 60 seconds.", exitCode = -1 };
            }

            await Task.WhenAll(outTask, errTask);
            
            string output = await outTask;
            string error = await errTask;

            return new { command = input.command, output = output, error = error, exitCode = process.ExitCode };
        }
    }
}
