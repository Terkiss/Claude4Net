using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    public enum SdkRuntime
    {
        Python,
        Node
    }

    internal sealed record SdkProcessRequest(
        SdkRuntime Runtime,
        string ScriptName,
        int Port,
        string ApiKey,
        string SuccessMarker,
        TimeSpan? Timeout = null,
        string? RequiredModule = null,
        TimeSpan? CapabilityTimeout = null);

    internal sealed record SdkProcessResult(string StandardOutput, string StandardError);

    internal static class SdkProcessRunner
    {
        internal const string ChildApiKeyEnvironmentVariable = "CLAUDE4NET_TEST_API_KEY";
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);
        internal static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);
        internal static readonly TimeSpan PipeDrainTimeout = TimeSpan.FromSeconds(2);

        internal static string ResolveScriptPath(string scriptName)
        {
            string scriptPath = Path.Combine(AppContext.BaseDirectory, scriptName);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"SDK process script was not copied to test output: '{scriptName}'.", scriptPath);
            }

            return scriptPath;
        }

        internal static ProcessStartInfo CreateStartInfo(string executable, SdkProcessRequest request)
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
            startInfo.ArgumentList.Add(ResolveScriptPath(request.ScriptName));
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.Environment[ChildApiKeyEnvironmentVariable] = request.ApiKey;
            return startInfo;
        }

        internal static async Task<SdkProcessResult> RunAsync(
            SdkProcessRequest request,
            IReadOnlyList<string>? runtimeCandidates = null,
            Func<string, string, TimeSpan, Task<SdkCapabilityResult>>? capabilityProbe = null)
        {
            IReadOnlyList<string> candidates = runtimeCandidates ?? SdkRuntimeDiscovery.GetRuntimeCandidates(request.Runtime);
            if (request.RequiredModule is not null)
            {
                string selected = await SelectRuntimeAsync(request, candidates, capabilityProbe);
                Process? selectedProcess = StartProcess(selected, request, out ProcessTreeJob? selectedProcessTree);
                if (selectedProcess is null)
                {
                    throw new SdkRuntimeUnavailableException(request.Runtime, [Redact(selected, request.ApiKey)]);
                }

                ProcessTreeJob containedProcessTree = selectedProcessTree!;
                using (selectedProcess)
                using (containedProcessTree)
                {
                    return await AwaitResultAsync(selectedProcess, containedProcessTree, request);
                }
            }

            foreach (string candidate in candidates)
            {
                Process? process = StartProcess(candidate, request, out ProcessTreeJob? processTree);
                if (process is null)
                {
                    continue;
                }

                ProcessTreeJob containedProcessTree = processTree!;
                using (process)
                using (containedProcessTree)
                {
                    return await AwaitResultAsync(process, containedProcessTree, request);
                }
            }

            throw new SdkRuntimeUnavailableException(
                request.Runtime,
                candidates.Select(candidate => Redact(candidate, request.ApiKey)).ToArray());
        }

        internal static async Task<string> SelectRuntimeAsync(
            SdkProcessRequest request,
            IReadOnlyList<string> candidates,
            Func<string, string, TimeSpan, Task<SdkCapabilityResult>>? capabilityProbe = null)
        {
            string requiredModule = request.RequiredModule ?? throw new ArgumentException("A required module is needed for capability selection.", nameof(request));
            capabilityProbe ??= (candidate, module, probeTimeout) =>
                SdkRuntimeCapabilityProbe.ProbeAsync(candidate, module, probeTimeout);
            TimeSpan timeout = request.CapabilityTimeout ?? SdkRuntimeCapabilityProbe.DefaultTimeout;
            var missingDependencies = new List<string>();

            foreach (string candidate in candidates)
            {
                SdkCapabilityResult result = await capabilityProbe(candidate, requiredModule, timeout);
                if (result == SdkCapabilityResult.Available)
                {
                    return candidate;
                }

                if (result == SdkCapabilityResult.MissingDependency)
                {
                    missingDependencies.Add(Redact(candidate, request.ApiKey));
                }
            }

            if (missingDependencies.Count > 0)
            {
                throw new SdkDependencyUnavailableException(requiredModule, missingDependencies);
            }

            throw new SdkRuntimeUnavailableException(
                request.Runtime,
                candidates.Select(candidate => Redact(candidate, request.ApiKey)).ToArray());
        }

        private static Process? StartProcess(
            string executable,
            SdkProcessRequest request,
            out ProcessTreeJob? processTree)
        {
            var process = new Process { StartInfo = CreateStartInfo(executable, request) };
            processTree = null;
            try
            {
                processTree = ProcessTreeJob.StartContained(process);
                return process;
            }
            catch (Win32Exception)
            {
                process.Dispose();
                return null;
            }

        }

        private static async Task<SdkProcessResult> AwaitResultAsync(
            Process process,
            ProcessTreeJob processTree,
            SdkProcessRequest request)
        {
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            TimeSpan timeout = request.Timeout ?? DefaultTimeout;

            try
            {
                await process.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                (string timedOutOutput, string timedOutError) = await TerminateAndDrainAsync(
                    process,
                    processTree,
                    standardOutputTask,
                    standardErrorTask);
                timedOutOutput = Redact(timedOutOutput, request.ApiKey);
                timedOutError = Redact(timedOutError, request.ApiKey);
                throw new SdkProcessTimeoutException(timeout, timedOutOutput, timedOutError);
            }

            (string drainedOutput, string drainedError) = await DrainAfterExitAsync(
                process,
                processTree,
                standardOutputTask,
                standardErrorTask);
            string standardOutput = Redact(drainedOutput, request.ApiKey);
            string standardError = Redact(drainedError, request.ApiKey);
            if (process.ExitCode != 0)
            {
                throw new SdkProcessExitException(process.ExitCode, standardOutput, standardError);
            }

            if (!standardOutput.Contains(request.SuccessMarker, StringComparison.Ordinal))
            {
                throw new SdkProcessAssertionException(request.SuccessMarker, standardOutput, standardError);
            }

            return new SdkProcessResult(standardOutput, standardError);
        }

        internal static async Task<(string StandardOutput, string StandardError)> DrainAfterExitAsync(
            Process process,
            ProcessTreeJob processTree,
            Task<string> standardOutputTask,
            Task<string> standardErrorTask)
        {
            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask).WaitAsync(PipeDrainTimeout);
            }
            catch (TimeoutException)
            {
                processTree.Kill(process);
                throw new SdkProcessCleanupTimeoutException("stdout/stderr pipe drain", PipeDrainTimeout);
            }

            return (await standardOutputTask, await standardErrorTask);
        }

        internal static async Task<(string StandardOutput, string StandardError)> TerminateAndDrainAsync(
            Process process,
            ProcessTreeJob processTree,
            Task<string> standardOutputTask,
            Task<string> standardErrorTask)
        {
            if (!process.HasExited)
            {
                processTree.Kill(process);
            }

            try
            {
                await process.WaitForExitAsync().WaitAsync(TerminationTimeout);
            }
            catch (TimeoutException)
            {
                throw new SdkProcessCleanupTimeoutException("process termination", TerminationTimeout);
            }

            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask).WaitAsync(PipeDrainTimeout);
            }
            catch (TimeoutException)
            {
                throw new SdkProcessCleanupTimeoutException("stdout/stderr pipe drain", PipeDrainTimeout);
            }

            return (await standardOutputTask, await standardErrorTask);
        }

        private static string Redact(string value, string secret)
        {
            return string.IsNullOrEmpty(secret)
                ? value
                : value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }
    }
}
