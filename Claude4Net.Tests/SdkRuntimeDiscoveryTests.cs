using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Claude4Net.Tests
{
    public class SdkRuntimeDiscoveryTests
    {
        private const string SecretApiKey = "c4n-sk-discovery-secret";

        [Fact]
        public void GetRuntimeCandidates_OnWindows_ReturnsEverySameNamePathExecutable()
        {
            using var pathFixture = new RuntimePathFixture("python.exe", "python.exe");
            string? ReadEnvironment(string name) => name switch
            {
                "PATH" => pathFixture.PathValue,
                "PATHEXT" => ".EXE;.CMD",
                _ => null
            };

            string[] candidates = SdkRuntimeDiscovery.GetRuntimeCandidates(
                SdkRuntime.Python,
                ReadEnvironment,
                isWindows: true,
                File.Exists);

            Assert.Equal(pathFixture.Files, candidates);
            Assert.All(candidates, candidate => Assert.True(Path.IsPathFullyQualified(candidate)));
        }

        [Fact]
        public void GetRuntimeCandidates_OnUnix_ReturnsPython3BeforePythonAndRequiresExecutableAccess()
        {
            using var pathFixture = new RuntimePathFixture("python", "python3", "python3");
            string? ReadEnvironment(string name) => name == "PATH" ? pathFixture.PathValue : null;
            bool IsExecutable(string path) =>
                File.Exists(path) && !path.EndsWith(Path.Combine("0", "python"), StringComparison.Ordinal);

            string[] candidates = SdkRuntimeDiscovery.GetRuntimeCandidates(
                SdkRuntime.Python,
                ReadEnvironment,
                isWindows: false,
                IsExecutable);

            Assert.Equal([pathFixture.Files[1], pathFixture.Files[2]], candidates);
        }

        [Fact]
        public void GetRuntimeCandidates_WhenOverrideResolves_PlacesItBeforePathDefaults()
        {
            using var pathFixture = new RuntimePathFixture("custom.exe", "python.exe");
            string? ReadEnvironment(string name) => name switch
            {
                "CLAUDE4NET_TEST_PYTHON" => "custom",
                "PATH" => pathFixture.PathValue,
                "PATHEXT" => ".EXE",
                _ => null
            };

            string[] candidates = SdkRuntimeDiscovery.GetRuntimeCandidates(
                SdkRuntime.Python,
                ReadEnvironment,
                isWindows: true,
                File.Exists);

            Assert.Equal(pathFixture.Files, candidates);
        }

        [Fact]
        public async Task SelectRuntimeAsync_WhenFirstPathPythonLacksModule_SelectsSecondCapablePython()
        {
            string[] candidates = ["first-python", "second-python"];
            var probed = new List<string>();
            var request = CreateRequest(requiredModule: "openai");

            string selected = await SdkProcessRunner.SelectRuntimeAsync(
                request,
                candidates,
                (candidate, _, _) =>
                {
                    probed.Add(candidate);
                    return Task.FromResult(candidate == candidates[1]
                        ? SdkCapabilityResult.Available
                        : SdkCapabilityResult.MissingDependency);
                });

            Assert.Equal(candidates[1], selected);
            Assert.Equal(candidates, probed);
        }

        [Fact]
        public async Task SelectRuntimeAsync_WhenOverrideLacksModule_ChecksPathFallbackNext()
        {
            string[] candidates = ["override-python", "path-python"];
            var request = CreateRequest(requiredModule: "openai");

            string selected = await SdkProcessRunner.SelectRuntimeAsync(
                request,
                candidates,
                (candidate, _, _) => Task.FromResult(candidate == "path-python"
                    ? SdkCapabilityResult.Available
                    : SdkCapabilityResult.MissingDependency));

            Assert.Equal("path-python", selected);
        }

        [Fact]
        public async Task SelectRuntimeAsync_WhenNoCandidateHasModule_ThrowsDependencyUnavailable()
        {
            string[] candidates = ["first-python", "second-python"];
            var request = CreateRequest(requiredModule: "openai");

            var exception = await Assert.ThrowsAsync<SdkDependencyUnavailableException>(() =>
                SdkProcessRunner.SelectRuntimeAsync(
                    request,
                    candidates,
                    (_, _, _) => Task.FromResult(SdkCapabilityResult.MissingDependency)));

            Assert.Equal("openai", exception.RequiredModule);
            Assert.All(candidates, candidate => Assert.Contains(candidate, exception.Message));
            Assert.DoesNotContain(SecretApiKey, exception.ToString());
        }

        [Fact]
        public async Task SelectRuntimeAsync_WhenExecutablesAreMissing_ThrowsRuntimeUnavailable()
        {
            string[] candidates = ["first-missing-python", "second-missing-python"];
            var request = CreateRequest(requiredModule: "openai");

            var exception = await Assert.ThrowsAsync<SdkRuntimeUnavailableException>(() =>
                SdkProcessRunner.SelectRuntimeAsync(
                    request,
                    candidates,
                    (_, _, _) => Task.FromResult(SdkCapabilityResult.MissingExecutable)));

            Assert.All(candidates, candidate => Assert.Contains(candidate, exception.Message));
            Assert.DoesNotContain(SecretApiKey, exception.ToString());
        }

        [Fact]
        public void CreateCapabilityStartInfo_DoesNotReceiveApiKey()
        {
            ProcessStartInfo startInfo = SdkRuntimeCapabilityProbe.CreateStartInfo("python", "openai");

            Assert.DoesNotContain(SecretApiKey, startInfo.ArgumentList);
            Assert.False(startInfo.Environment.ContainsKey(SdkProcessRunner.ChildApiKeyEnvironmentVariable));
            Assert.Equal("-c", startInfo.ArgumentList[0]);
            Assert.Equal("openai", startInfo.ArgumentList[2]);
        }

        [Fact]
        public void CapabilityPreflight_DefaultTimeoutIsBounded()
        {
            Assert.Equal(TimeSpan.FromSeconds(5), SdkRuntimeCapabilityProbe.DefaultTimeout);
        }

        [Fact]
        public async Task CapabilityProbe_DoesNotExecuteProbeBeforeContainmentRelease()
        {
            ProcessStartInfo startInfo = SdkRuntimeCapabilityProbe.CreateStartInfo(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python)[0],
                "json");
            using var process = new Process { StartInfo = startInfo };
            using ProcessTreeJob processTree = ProcessTreeJob.StartContained(process, releaseGate: false);
            Task<string?> firstActionTask = process.StandardOutput.ReadLineAsync();

            await Task.Delay(250);

            Assert.False(firstActionTask.IsCompleted);
            processTree.ReleaseGate(process);
            Assert.Equal("CAPABILITY_PROBE_FIRST_ACTION", await firstActionTask.WaitAsync(TimeSpan.FromSeconds(2)));
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(0, process.ExitCode);
        }

        [Fact]
        public async Task CapabilityProbe_TimeoutHasBoundedTerminationAndDrain()
        {
            TimeSpan capabilityTimeout = TimeSpan.FromMilliseconds(250);
            var stopwatch = Stopwatch.StartNew();

            Exception? exception = await Record.ExceptionAsync(() => SdkRuntimeCapabilityProbe.ProbeAsync(
                SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python)[0],
                "json",
                capabilityTimeout,
                startInfo => startInfo.Environment["CLAUDE4NET_CAPABILITY_PROBE_HOLD_PIPES"] = "1"));
            stopwatch.Stop();

            Assert.True(
                exception is SdkCapabilityTimeoutException or SdkProcessCleanupTimeoutException,
                $"Expected a capability timeout, but received {exception?.GetType().Name ?? "no exception"}.");
            Assert.True(
                stopwatch.Elapsed < capabilityTimeout + TimeSpan.FromSeconds(7),
                $"Bounded capability cleanup returned after {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task CapabilityProbe_WhenParentExitsButDescendantHoldsPipes_BoundsPipeDrain()
        {
            TimeSpan capabilityTimeout = TimeSpan.FromMilliseconds(750);
            TimeSpan completionBound = capabilityTimeout + SdkProcessRunner.PipeDrainTimeout + TimeSpan.FromSeconds(3);
            var stopwatch = Stopwatch.StartNew();

            Exception? exception = await Record.ExceptionAsync(() => SdkRuntimeCapabilityProbe.ProbeAsync(
                    SdkRuntimeDiscovery.GetRuntimeCandidates(SdkRuntime.Python)[0],
                    "json",
                    capabilityTimeout,
                    startInfo => startInfo.ArgumentList[1] =
                        "input();import subprocess,sys;print('CAPABILITY_PROBE_FIRST_ACTION',flush=True);subprocess.Popen([sys.executable,'-c','import time;time.sleep(30)']);sys.exit(0)"))
                .WaitAsync(completionBound);
            stopwatch.Stop();

            Assert.True(
                exception is SdkProcessCleanupTimeoutException or SdkCapabilityTimeoutException,
                $"Expected a timeout exception, but received {exception?.GetType().Name ?? "no exception"}.");
            Assert.True(stopwatch.Elapsed < completionBound, $"Bounded capability pipe drain returned after {stopwatch.Elapsed}.");
        }

        [Fact]
        public async Task RunAsync_WhenCapableRuntimeBlackBoxFails_DoesNotTryAnotherRuntime()
        {
            var request = CreateRequest(port: -2, requiredModule: "json");
            int probeCount = 0;

            var exception = await Assert.ThrowsAsync<SdkProcessExitException>(() =>
                SdkProcessRunner.RunAsync(
                    request,
                    ["python", "runtime-that-must-not-be-tried"],
                    (_, _, _) =>
                    {
                        probeCount++;
                        return Task.FromResult(SdkCapabilityResult.Available);
                    }));

            Assert.Equal(7, exception.ExitCode);
            Assert.Equal(1, probeCount);
        }

        private static SdkProcessRequest CreateRequest(
            int port = -4,
            string? requiredModule = null)
        {
            return new SdkProcessRequest(
                SdkRuntime.Python,
                "sdk_process_fixture.py",
                port,
                SecretApiKey,
                "FIXTURE_OK",
                RequiredModule: requiredModule);
        }

        private sealed class RuntimePathFixture : IDisposable
        {
            private readonly string _root;

            internal RuntimePathFixture(params string[] fileNames)
            {
                _root = Path.Combine(Path.GetTempPath(), $"claude4net-runtime-{Guid.NewGuid():N}");
                Files = new string[fileNames.Length];
                var directories = new string[fileNames.Length];
                for (int index = 0; index < fileNames.Length; index++)
                {
                    directories[index] = Path.Combine(_root, index.ToString());
                    Directory.CreateDirectory(directories[index]);
                    Files[index] = Path.Combine(directories[index], fileNames[index]);
                    File.WriteAllText(Files[index], string.Empty);
                }

                PathValue = string.Join(Path.PathSeparator, directories);
            }

            internal string[] Files { get; }
            internal string PathValue { get; }

            public void Dispose()
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
