using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Claude4Net.Tests
{
    public class SdkProcessRunnerTests
    {
        private const string FixtureScript = "sdk_process_fixture.py";
        private const string SecretApiKey = "c4n-sk-runner-secret-123";

        [Theory]
        [InlineData("blackbox_openai_sdk_runner.py")]
        [InlineData("blackbox_openai_js_sdk_runner.mjs")]
        public void ResolveScriptPath_UsesTestOutputDirectory(string scriptName)
        {
            string path = SdkProcessRunner.ResolveScriptPath(scriptName);

            Assert.Equal(Path.Combine(AppContext.BaseDirectory, scriptName), path);
            Assert.True(File.Exists(path), $"Expected copied SDK script at '{path}'.");
        }

        [Fact]
        public void CreateStartInfo_PassesApiKeyOnlyThroughEnvironment()
        {
            var request = CreateRequest(port: -4, successMarker: "FIXTURE_OK");

            ProcessStartInfo startInfo = SdkProcessRunner.CreateStartInfo("python", request);

            Assert.DoesNotContain(startInfo.ArgumentList, argument => argument.Contains(SecretApiKey, StringComparison.Ordinal));
            Assert.DoesNotContain("--api-key", startInfo.ArgumentList);
            Assert.Equal(SecretApiKey, startInfo.Environment[SdkProcessRunner.ChildApiKeyEnvironmentVariable]);
        }

        [Fact]
        public async Task RunAsync_WhenRuntimeIsMissing_ThrowsRuntimeUnavailable()
        {
            var request = CreateRequest(port: -4, successMarker: "FIXTURE_OK");

            var exception = await Assert.ThrowsAsync<SdkRuntimeUnavailableException>(() =>
                SdkProcessRunner.RunAsync(request, ["claude4net-runtime-that-does-not-exist"]));

            Assert.Contains("claude4net-runtime-that-does-not-exist", exception.Message);
            Assert.DoesNotContain(SecretApiKey, exception.ToString());
        }

        [Fact]
        public async Task RunAsync_WhenProcessExitsNonzero_ThrowsProcessExitFailure()
        {
            var request = CreateRequest(port: -2, successMarker: "FIXTURE_OK");

            var exception = await Assert.ThrowsAsync<SdkProcessExitException>(() => SdkProcessRunner.RunAsync(request));

            Assert.Equal(7, exception.ExitCode);
            Assert.Contains("fixture failure", exception.StandardError);
        }

        [Fact]
        public async Task RunAsync_WhenSuccessAssertionIsMissing_ThrowsAssertionFailure()
        {
            var request = CreateRequest(port: -3, successMarker: "EXPECTED_MARKER");

            var exception = await Assert.ThrowsAsync<SdkProcessAssertionException>(() => SdkProcessRunner.RunAsync(request));

            Assert.Contains("EXPECTED_MARKER", exception.Message);
            Assert.DoesNotContain(SecretApiKey, exception.ToString());
        }

        [Fact]
        public async Task RunAsync_DrainsStdoutAndStderrConcurrently()
        {
            var request = CreateRequest(port: -4, successMarker: "FIXTURE_OK");

            SdkProcessResult result = await SdkProcessRunner.RunAsync(request);

            Assert.Contains("FIXTURE_OK", result.StandardOutput);
            Assert.Contains("stderr-19999", result.StandardError);
        }

        [Fact]
        public async Task RunAsync_WhenTimedOut_KillsEntireProcessTree()
        {
            var request = CreateRequest(
                port: -1,
                successMarker: "NEVER_REACHED",
                timeout: TimeSpan.FromMilliseconds(750));
            var stopwatch = Stopwatch.StartNew();

            var exception = await Assert.ThrowsAsync<SdkProcessTimeoutException>(() => SdkProcessRunner.RunAsync(request));
            stopwatch.Stop();
            int childProcessId = int.Parse(
                exception.StandardOutput
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .Single(line => line.StartsWith("CHILD_PID=", StringComparison.Ordinal))
                    .AsSpan("CHILD_PID=".Length));

            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Timeout returned after {stopwatch.Elapsed}.");
            Assert.True(SpinWait.SpinUntil(() => HasExited(childProcessId), TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public async Task ContainedStart_DoesNotReleaseFixtureCodeBeforeJobAttachment()
        {
            ProcessStartInfo startInfo = SdkProcessRunner.CreateStartInfo(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python).First(),
                CreateRequest(port: -4, successMarker: "FIXTURE_OK"));
            using var process = new Process { StartInfo = startInfo };
            using ProcessTreeJob processTree = ProcessTreeJob.StartContained(process, releaseGate: false);
            Task<string?> firstActionTask = process.StandardOutput.ReadLineAsync();

            await Task.Delay(250);

            Assert.False(firstActionTask.IsCompleted);
            if (OperatingSystem.IsWindows())
            {
                Assert.True(processTree.Contains(process));
            }

            processTree.ReleaseGate(process);
            Assert.Equal("FIXTURE_FIRST_ACTION", await firstActionTask.WaitAsync(TimeSpan.FromSeconds(2)));
            processTree.Kill(process);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task ContainedStart_WhenStdinClosesWithoutReleaseByte_DoesNotRunFixtureCode()
        {
            ProcessStartInfo startInfo = SdkProcessRunner.CreateStartInfo(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python).First(),
                CreateRequest(port: -3, successMarker: "EXPECTED_MARKER"));
            using var process = new Process { StartInfo = startInfo };
            using ProcessTreeJob processTree = ProcessTreeJob.StartContained(process, releaseGate: false);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();

            process.StandardInput.Close();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain("FIXTURE_FIRST_ACTION", await outputTask);
        }

        [Fact]
        public async Task ContainedStart_WhenGateByteIsUnexpected_DoesNotRunFixtureCode()
        {
            ProcessStartInfo startInfo = SdkProcessRunner.CreateStartInfo(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python).First(),
                CreateRequest(port: -3, successMarker: "EXPECTED_MARKER"));
            using var process = new Process { StartInfo = startInfo };
            using ProcessTreeJob processTree = ProcessTreeJob.StartContained(process, releaseGate: false);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync();

            process.StandardInput.BaseStream.WriteByte(2);
            process.StandardInput.Close();

            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.DoesNotContain("FIXTURE_FIRST_ACTION", await outputTask);
        }

        [Fact]
        public async Task RunAsync_WhenTimedOut_BoundsTerminationAndPipeDrain()
        {
            TimeSpan requestTimeout = TimeSpan.FromMilliseconds(250);
            var request = CreateRequest(
                port: -5,
                successMarker: "NEVER_REACHED",
                timeout: requestTimeout);
            var stopwatch = Stopwatch.StartNew();

            Exception? exception = await Record.ExceptionAsync(() => SdkProcessRunner.RunAsync(request));
            stopwatch.Stop();

            Assert.True(
                exception is SdkProcessTimeoutException or SdkProcessCleanupTimeoutException,
                $"Expected a process timeout, but received {exception?.GetType().Name ?? "no exception"}.");
            Assert.True(
                stopwatch.Elapsed < requestTimeout + TimeSpan.FromSeconds(7),
                $"Bounded cleanup returned after {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task RunAsync_WhenParentExitsButDescendantHoldsPipes_BoundsPipeDrain()
        {
            TimeSpan requestTimeout = TimeSpan.FromMilliseconds(250);
            TimeSpan completionBound = requestTimeout + SdkProcessRunner.PipeDrainTimeout + TimeSpan.FromSeconds(2);
            var request = CreateRequest(
                port: -6,
                successMarker: "FIXTURE_OK",
                timeout: requestTimeout);
            var stopwatch = Stopwatch.StartNew();

            Exception? exception = await Record.ExceptionAsync(() => SdkProcessRunner.RunAsync(request))
                .WaitAsync(completionBound);
            stopwatch.Stop();

            var cleanupException = Assert.IsType<SdkProcessCleanupTimeoutException>(exception);
            Assert.Equal("stdout/stderr pipe drain", cleanupException.Phase);
            Assert.Equal(SdkProcessRunner.PipeDrainTimeout, cleanupException.Bound);
            Assert.True(stopwatch.Elapsed < completionBound, $"Bounded pipe drain returned after {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task RunAsync_WhenContainmentAttachmentFails_NeverReleasesGate()
        {
            ProcessStartInfo startInfo = SdkProcessRunner.CreateStartInfo(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python).First(),
                CreateRequest(port: -4, successMarker: "FIXTURE_OK"));
            using var process = new Process { StartInfo = startInfo };
            int processId = 0;
            Task<string>? outputTask = null;

            Assert.Throws<InvalidOperationException>(() => ProcessTreeJob.StartContained(
                process,
                attach: startedProcess =>
                {
                    processId = startedProcess.Id;
                    outputTask = startedProcess.StandardOutput.ReadToEndAsync();
                    throw new InvalidOperationException("Injected containment attachment failure.");
                }));

            Assert.NotEqual(0, processId);
            Assert.True(SpinWait.SpinUntil(() => HasExited(processId), TimeSpan.FromSeconds(2)));
            Assert.DoesNotContain(
                "FIXTURE_FIRST_ACTION",
                await outputTask!.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        [Fact]
        public void DefaultTimeout_IsSixtySeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), SdkProcessRunner.DefaultTimeout);
        }

        private static SdkProcessRequest CreateRequest(
            int port,
            string successMarker,
            TimeSpan? timeout = null)
        {
            return new SdkProcessRequest(
                SdkRuntime.Python,
                FixtureScript,
                port,
                SecretApiKey,
                successMarker,
                timeout);
        }

        private static bool HasExited(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                return process.HasExited;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
    }
}
