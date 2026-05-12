using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Commands;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class K029K030SecurityTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _workspace;
        private readonly string _sessionId = "test-session";

        public K029K030SecurityTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "Claude4NetSecurityTests-" + Guid.NewGuid().ToString("N"));
            _workspace = Path.Combine(_testDir, "workspace");
            Directory.CreateDirectory(_workspace);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, true);
        }

        [Fact]
        public async Task Checkpoint_RestoreRejectsTraversalCheckpointId()
        {
            var store = new CheckpointStore(_workspace, _sessionId);
            await Assert.ThrowsAsync<SecurityException>(() => store.RestoreCheckpointAsync("../../../etc/passwd"));
            await Assert.ThrowsAsync<SecurityException>(() => store.RestoreCheckpointAsync("..\\..\\secret.txt"));
        }

        [Fact]
        public async Task Checkpoint_RejectsSiblingPrefixAbsolutePath()
        {
            string secretDir = Path.Combine(_testDir, "workspace_secret");
            Directory.CreateDirectory(secretDir);
            string secretFile = Path.Combine(secretDir, "confidential.txt");
            File.WriteAllText(secretFile, "secret");

            var store = new CheckpointStore(_workspace, _sessionId);
            await Assert.ThrowsAsync<SecurityException>(() => store.CreateCheckpointAsync("tc1", "tool", new List<string> { secretFile }));
        }

        [Fact]
        public async Task Handoff_RejectsEvidencePathTraversal()
        {
            var store = new HandoffStore(_workspace, _sessionId);
            await Assert.ThrowsAsync<SecurityException>(() => store.AddEvidenceAsync("../outside.txt", "data"));
            await Assert.ThrowsAsync<SecurityException>(() => store.AddEvidenceAsync("subdir/nested.txt", "data"));
            await Assert.ThrowsAsync<SecurityException>(() => store.AddEvidenceAsync("C:\\windows\\win.ini", "data"));
        }

        [Fact]
        public async Task Checkpoint_GetSafeCheckpointDir_NormalizedCheckpointsDir_Check()
        {
            var store = new CheckpointStore(_workspace, _sessionId);
            await Assert.ThrowsAsync<SecurityException>(() => store.SaveDiffAsync("invalid!id", "diff"));
        }

        [Fact]
        public void CheckpointStore_RejectsTraversalSessionId()
        {
            Assert.Throws<ArgumentException>(() => new CheckpointStore(_workspace, "../evil"));
        }

        [Fact]
        public void HandoffStore_RejectsTraversalSessionId()
        {
            Assert.Throws<ArgumentException>(() => new HandoffStore(_workspace, "invalid/session"));
        }

        [Fact]
        public void HandoffStore_RejectsRootedSessionId()
        {
            string rootedPath = Path.DirectorySeparatorChar + "secret-area";
            Assert.Throws<ArgumentException>(() => new HandoffStore(_workspace, rootedPath));
        }

        [Fact]
        public void CheckpointStore_SessionPathStaysUnderSessionsRoot()
        {
            Assert.Throws<ArgumentException>(() => new CheckpointStore(_workspace, "session:name"));
        }

        // --- Regression Tests ---

        [Fact]
        public async Task EnvCommand_AllStillMasksSecrets()
        {
            var sc = new ServiceCollection();
            var services = sc.BuildServiceProvider();
            string key = "MY_TEST_SECRET_KEY";
            string val = "sk-ant-api03-VERY-SECRET-TOKEN-12345";
            Environment.SetEnvironmentVariable(key, val);
            try
            {
                var cmd = CommandRegistry.FindCommand("env");
                string result = await cmd.Handler!("all", services);

                Assert.Contains("MY_TEST_SECRET_KEY", result);
                Assert.DoesNotContain("VERY-SECRET-TOKEN", result);
                // SourceGuard pattern matching replaces with ****
                Assert.Contains("****", result);
            }
            finally { Environment.SetEnvironmentVariable(key, null); }
        }

        [Fact]
        public async Task DoctorCommand_StillReportsDbPluginsAndSkillRegistry()
        {
            var sc = new ServiceCollection();
            sc.AddSingleton<ISmartRouter, SmartRouter>();
            var mockRegistry = new SkillRegistryService(_workspace);
            sc.AddSingleton(mockRegistry);
            var services = sc.BuildServiceProvider();

            var cmd = CommandRegistry.FindCommand("doctor");
            string result = await cmd.Handler!("", services);

            Assert.Contains("Security Audit:", result);
            Assert.Contains("Plugins:", result);
            Assert.Contains("Skill Registry:", result);
        }

        [Fact]
        public async Task CoordinateStatus_StillShowsEvidenceAndReadiness()
        {
            var taskId = "T-REG-1";
            var store = CoordinatorStore.Instance;
            if (AppState.Tasks.ContainsKey(taskId)) AppState.Tasks.TryRemove(taskId, out _);
            store.CreateTask(taskId, "Test Task", "Description");
            store.AddEvidence(taskId, "DesignDoc", "Tester", "Summary Evidence", "Details");

            var services = new ServiceCollection().BuildServiceProvider();
            var cmd = CommandRegistry.FindCommand("coordinate");
            string result = await cmd.Handler!($"status {taskId}", services);

            Assert.Contains("Merge Readiness:", result);
            Assert.Contains("Evidence: Summary Evidence", result);
        }
    }
}
