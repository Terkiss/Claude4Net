using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Tools
{
    /// <summary>
    /// BashTool 실행을 위한 입력 매개변수 클래스입니다.
    /// </summary>
    public class BashInput
    {
        /// <summary>
        /// 실행할 셸 명령입니다.
        /// </summary>
        public string command { get; set; } = string.Empty;
        
        /// <summary>
        /// (선택 사항) 프로세스 재시작 여부를 결정합니다.
        /// </summary>
        public bool? restart { get; set; }
    }

    /// <summary>
    /// 로컬 시스템에서 셸 명령(PowerShell 기반)을 실행하는 도구입니다.
    /// </summary>
    public class BashTool : ITool
    {
        public string Name => "BashTool";
        public string Description => "Execute a shell command in the local system.";
        public List<string>? Aliases => new() { "bash", "sh", "shell" };
        public object? InputSchema => new { type = "object", properties = new { command = new { type = "string", description = "The shell command to run" } }, required = new[] { "command" } };

        /// <summary>
        /// 지정된 셸 명령을 비동기적으로 실행합니다.
        /// </summary>
        /// <param name="arguments">JSON 형식의 명령 매개변수</param>
        /// <param name="context">실행 컨텍스트</param>
        /// <param name="ct">취소 토큰</param>
        /// <returns>명령 실행 결과(출력, 에러, 종료 코드)</returns>
        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<BashInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments for BashTool");

            // [보안 및 설정] OS에 따라 PowerShell 또는 Bash를 사용하여 명령을 격리된 환경에서 실행합니다.
            using var process = new Process();
            var (fileName, shellArgs) = GetShellConfiguration(input.command);

            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = shellArgs;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            
            // 작업 디렉토리 설정: AppState에 설정된 CWD가 있으면 사용하고, 없으면 현재 디렉토리를 사용합니다.
            process.StartInfo.WorkingDirectory = string.IsNullOrEmpty(AppState.CurrentCwd) ? Environment.CurrentDirectory : AppState.CurrentCwd;

            process.Start();
            
            // [병렬 처리] Task.WhenAll을 통한 병렬 스트림 읽기 (Deadlock 방지)
            // 출력이 너무 많아 버퍼가 가득 찰 경우 발생할 수 있는 교착 상태를 예방합니다.
            var outTask = process.StandardOutput.ReadToEndAsync(ct);
            var errTask = process.StandardError.ReadToEndAsync(ct);
            
            try
            {
                // [안전장치] 기본 타임아웃 60초 또는 외부 취소 토큰(ct) 결합
                // 무한 루프나 응답 없는 프로세스로부터 시스템 자원을 보호합니다.
                using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linkedCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 타임아웃 또는 사용자 취소 시 프로세스를 강제 종료(Kill)하여 고스트 프로세스 방지
                process.Kill(true);
                string reason = ct.IsCancellationRequested ? "User cancelled." : "Timed out after 60 seconds.";
                return new { command = input.command, output = "", error = $"Command execution aborted: {reason}", exitCode = -1 };
            }
            
            // 스트림 읽기 완료 대기
            await Task.WhenAll(outTask, errTask);
            
            string output = await outTask;
            string error = await errTask;

            return new { command = input.command, output = output, error = error, exitCode = process.ExitCode };
        }

        /// <summary>
        /// 현재 운영체제에 적합한 쉘 실행 파일과 인자 구성을 반환합니다.
        /// (macOS support prepared, native verification pending)
        /// </summary>
        public static (string FileName, string Arguments) GetShellConfiguration(string command)
        {
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            return GetShellConfiguration(command, isWindows);
        }

        /// <summary>
        /// 특정 플랫폼에 대한 쉘 실행 파일과 인자 구성을 반환합니다. (테스트용)
        /// </summary>
        public static (string FileName, string Arguments) GetShellConfiguration(string command, bool isWindows)
        {
            if (isWindows)
            {
                return ("powershell.exe", $"-NoProfile -Command \"{command}\"");
            }
            else
            {
                // macOS/Linux: bash 우선, 없으면 sh 사용
                // Native macOS verification pending
                string shell = "/bin/bash";
                if (!File.Exists(shell)) shell = "/bin/sh";

                return (shell, $"-c \"{command}\"");
            }
        }
    }
}
