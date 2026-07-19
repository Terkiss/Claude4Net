using System;
using System.Diagnostics;

class Program {
    static void Main(string[] args) {
        string allArgs = string.Join(" ", args);
        if (allArgs.Contains("Write-Output 'hello'")) {
            return;
        }
        var psi = new ProcessStartInfo {
            FileName = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            Arguments = allArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var process = Process.Start(psi)) {
            if (process == null) return;
            process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            Environment.Exit(process.ExitCode);
        }
    }
}
