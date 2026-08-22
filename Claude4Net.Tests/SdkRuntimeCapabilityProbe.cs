using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    internal enum SdkCapabilityResult
    {
        Available,
        MissingExecutable,
        MissingDependency
    }

    internal static class SdkRuntimeCapabilityProbe
    {
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        internal static ProcessStartInfo CreateStartInfo(string executable, string requiredModule)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("input();import importlib.util,os,subprocess,sys,time;print('CAPABILITY_PROBE_FIRST_ACTION',flush=True);hold=os.environ.get('CLAUDE4NET_CAPABILITY_PROBE_HOLD_PIPES');subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)']) if hold else None;time.sleep(30) if hold else None;sys.exit(0 if importlib.util.find_spec(sys.argv[1]) else 3)");
            startInfo.ArgumentList.Add(requiredModule);
            startInfo.Environment.Remove(SdkProcessRunner.ChildApiKeyEnvironmentVariable);
            return startInfo;
        }

        internal static async Task<SdkCapabilityResult> ProbeAsync(
            string executable,
            string requiredModule,
            TimeSpan timeout,
            Action<ProcessStartInfo>? configureStartInfo = null)
        {
            ProcessStartInfo startInfo = CreateStartInfo(executable, requiredModule);
            configureStartInfo?.Invoke(startInfo);
            var process = new Process { StartInfo = startInfo };
            ProcessTreeJob? processTree;
            try
            {
                processTree = ProcessTreeJob.StartContained(process);
            }
            catch (Win32Exception)
            {
                process.Dispose();
                return SdkCapabilityResult.MissingExecutable;
            }

            using (process)
            using (processTree)
            {
                Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
                try
                {
                    await process.WaitForExitAsync().WaitAsync(timeout);
                }
                catch (TimeoutException)
                {
                    await SdkProcessRunner.TerminateAndDrainAsync(
                        process,
                        processTree,
                        standardOutputTask,
                        standardErrorTask);
                    throw new SdkCapabilityTimeoutException(executable, requiredModule, timeout);
                }

                (string standardOutput, string standardError) = await SdkProcessRunner.DrainAfterExitAsync(
                    process,
                    processTree,
                    standardOutputTask,
                    standardErrorTask);
                return process.ExitCode switch
                {
                    0 => SdkCapabilityResult.Available,
                    3 => SdkCapabilityResult.MissingDependency,
                    _ => throw new SdkCapabilityProbeException(
                        executable,
                        requiredModule,
                        process.ExitCode,
                        standardOutput,
                        standardError)
                };
            }
        }
    }
}
