using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TeruTeruPandas.Core;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K079SkillTrajectoryMiningIntegrationTests : IAsyncLifetime
    {
        private readonly string _tempWorkspace;
        private readonly SkillRegistryService _registry;
        private readonly SkillProposalService _proposalService;
        private string? _originalCwd;
        private string? _originalSessionId;

        public K079SkillTrajectoryMiningIntegrationTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_TrajectoryTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            _registry = new SkillRegistryService(_tempWorkspace);
            _proposalService = new SkillProposalService(_registry);
        }

        public async Task InitializeAsync()
        {
            _originalCwd = AppState.CurrentCwd;
            _originalSessionId = AppState.SessionId;

            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = "test-session-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
            await PandasUniverseManager.Instance.EnsureBaselineTablesAsync();
        }

        public async Task DisposeAsync()
        {
            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await Task.Delay(100);

            if (Directory.Exists(_tempWorkspace))
            {
                try
                {
                    Directory.Delete(_tempWorkspace, true);
                }
                catch { }
            }

            AppState.CurrentCwd = _originalCwd;
            AppState.SessionId = _originalSessionId ?? Guid.NewGuid().ToString();
        }

        [Fact]
        public async Task TrajectoryMiner_IntegrationPipeline_ShouldDiscoverRecordAndDeduplicateFailures()
        {
            var sessionId1 = "session-test-01";
            var sessionId2 = "session-test-02";

            // 1. Seed Failure 1: In agent_trajectories
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                var columns = new Dictionary<string, TeruTeruPandas.Core.Column.IColumn>
                {
                    ["Timestamp"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { DateTime.UtcNow.ToString("O") }),
                    ["AgentId"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { sessionId1 }),
                    ["ToolName"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "run_command" }),
                    ["IsError"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "True" }),
                    ["ErrorReason"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "CommandException: command not found" }),
                    ["Payload"] = new TeruTeruPandas.Core.Column.StringColumn(new[] { "{\"CommandLine\":\"dotnet non-existent\"}" })
                };
                var df = new DataFrame(columns);
                u.AddOrUpdateTable("agent_trajectories", df);
            });

            // 2. Seed Failure 2: In event store (session event store) using FileAgentEventStore
            var eventStore = new FileAgentEventStore(_tempWorkspace);

            var testFilePath = Path.Combine(_tempWorkspace, "test.txt").Replace("\\", "\\\\");
            // Append a failed tool invocation event to event store
            await eventStore.AppendEventAsync(sessionId2, new ToolCalledEvent
            {
                Version = 1,
                ToolUseId = "call-1",
                ToolName = "write_to_file",
                Arguments = "{\"TargetPath\":\"" + testFilePath + "\"}"
            });
            await eventStore.AppendEventAsync(sessionId2, new ToolResultEvent
            {
                Version = 2,
                ToolUseId = "call-1",
                Result = "UnauthorizedAccessException: Access is denied.",
                IsError = true
            });

            // 3. Seed Failure 3: Raw JSON RunError event to events.jsonl
            var session2Dir = Path.Combine(_tempWorkspace, ".claude4net", "sessions", sessionId2);
            Directory.CreateDirectory(session2Dir);
            var eventsPath = Path.Combine(session2Dir, "events.jsonl");

            var runErrorJson = "{\"Type\":\"RunErrorEvent\",\"Payload\":{\"ErrorMessage\":\"InvalidOperationException: Invalid state transition\"}}";
            await File.AppendAllLinesAsync(eventsPath, new[] { runErrorJson });

            // 4. Seed Failure 4: Verification Failure (failed check in verification-result.json)
            var verificationResult = new VerificationResult
            {
                VerifierSessionId = "verifier-test-01",
                GeneratorSessionId = sessionId1,
                Verdict = VerificationVerdict.Fail,
                Checks = new List<VerificationCheck>
                {
                    new VerificationCheck
                    {
                        Name = "Release Build Test",
                        Command = "dotnet build -c Release",
                        Result = VerificationVerdict.Fail,
                        Evidence = "Build failed with CS0103: The name 'Helper' does not exist in the current context",
                        CompletedAt = DateTimeOffset.UtcNow
                    }
                }
            };
            var verificationPath = Path.Combine(_tempWorkspace, ".claude4net", "sessions", sessionId1, "verification-result.json");
            Directory.CreateDirectory(Path.GetDirectoryName(verificationPath)!);
            await File.WriteAllTextAsync(verificationPath, JsonSerializer.Serialize(verificationResult));

            // Force SQLite save/flush
            await PandasUniverseManager.Instance.ExecuteAsync(u =>
            {
                PandasUniverseManager.Instance.GetStore(new WorkspaceStateContext
                {
                    WorkspaceRoot = _tempWorkspace,
                    SessionId = AppState.SessionId
                }).ForceSaveSync();
            });

            // 5. Run TrajectoryMiner
            var miner = new TrajectoryMiner(_tempWorkspace);

            // Check list of failures from MineFailurePatterns
            var patterns = await miner.MineFailurePatternsAsync();
            Assert.NotEmpty(patterns);
            Assert.Contains(patterns, p => p.Contains("run_command") && p.Contains("CommandException"));
            Assert.Contains(patterns, p => p.Contains("write_to_file") && p.Contains("UnauthorizedAccessException"));
            Assert.Contains(patterns, p => p.Contains("run") && p.Contains("InvalidOperationException"));
            Assert.Contains(patterns, p => p.Contains("verification_check") && p.Contains("CS0103"));

            // 6. Mine and Generate Proposals
            var newProposals = await miner.MineAndGenerateProposalsAsync(_proposalService);

            // Assert that proposals are generated in Draft/Proposed status and not applied
            Assert.NotEmpty(newProposals);
            foreach (var prop in newProposals)
            {
                Assert.Equal(SkillProposalStatus.Proposed, prop.Status);
                Assert.NotNull(prop.Id);
                Assert.NotEmpty(prop.EvidenceReferences);

                // Assert that metadata has evidence info
                Assert.True(prop.Metadata.ContainsKey("FailurePattern"));
                Assert.True(prop.Metadata.ContainsKey("SessionId"));
                Assert.True(prop.Metadata.ContainsKey("ErrorType"));
            }

            var generatedCount = newProposals.Count;

            // 7. Run again to verify Deduplication
            var secondProposals = await miner.MineAndGenerateProposalsAsync(_proposalService);
            Assert.Empty(secondProposals); // No new proposals should be generated

            // Reload from file to ensure they were persisted properly
            var reloadService = new SkillProposalService(_registry);
            await reloadService.LoadAsync(_tempWorkspace);
            var loadedList = reloadService.ListProposals();
            Assert.Equal(generatedCount, loadedList.Count);
        }
    }
}
