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
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K094SkillEvolutionTests : IAsyncLifetime
    {
        private readonly string _tempWorkspace;
        private SkillRegistryService _registry = null!;
        private SkillProposalService _proposalService = null!;
        private string? _originalCwd;
        private string? _originalSessionId;
        private IServiceProvider _serviceProvider = null!;

        public K094SkillEvolutionTests()
        {
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K094Test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
        }

        public async Task InitializeAsync()
        {
            _originalCwd = AppState.CurrentCwd;
            _originalSessionId = AppState.SessionId;

            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = "session-k094-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            await PandasUniverseManager.Instance.ResetAndFlushForTestAsync();
            await PandasUniverseManager.Instance.EnsureBaselineTablesAsync();

            _registry = new SkillRegistryService(_tempWorkspace);
            await _registry.LoadAsync();
            _proposalService = new SkillProposalService(_registry);
            await _proposalService.LoadAsync(_tempWorkspace);

            var services = new ServiceCollection();
            services.AddSingleton(_registry);
            services.AddSingleton(_proposalService);
            services.AddSingleton<SkillUsageRecorder>();
            _serviceProvider = services.BuildServiceProvider();
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
        public async Task SkillUsageRecorder_ShouldLogToFileAndUpdateMetrics()
        {
            // Arrange
            var recorder = _serviceProvider.GetRequiredService<SkillUsageRecorder>();
            recorder.WorkspaceRoot = _tempWorkspace;
            string skillId = "test-tool-1";

            // Act: Record 2 successes, 1 failure
            await recorder.RecordAsync(skillId, success: true, elapsedMs: 120, score: 90);
            await recorder.RecordAsync(skillId, success: true, elapsedMs: 80, score: 95);
            await recorder.RecordAsync(skillId, success: false, elapsedMs: 200, score: 20, error: "MockErrorException: Something failed");

            // Assert: verify .claude4net/skill-usage.jsonl exists and contains records
            string usageFile = Path.Combine(_tempWorkspace, ".claude4net", "skill-usage.jsonl");
            Assert.True(File.Exists(usageFile));

            var lines = File.ReadAllLines(usageFile);
            Assert.Equal(3, lines.Length);

            var lastRecord = JsonSerializer.Deserialize<SkillUsageRecorder.SkillUsageRecord>(lines[2]);
            Assert.NotNull(lastRecord);
            Assert.Equal(skillId, lastRecord.SkillId);
            Assert.False(lastRecord.Success);
            Assert.Equal(20, lastRecord.Score);
            Assert.Equal("MockErrorException: Something failed", lastRecord.Error);
        }

        [Fact]
        public async Task SkillUsageRecorder_ThresholdReached_ShouldTriggerProposalGeneration()
        {
            // Arrange
            var recorder = _serviceProvider.GetRequiredService<SkillUsageRecorder>();
            recorder.WorkspaceRoot = _tempWorkspace;
            string skillId = "test-tool-2";

            // Register the skill so metrics/proposal target has context
            var skillRecord = new SkillRegistryRecord
            {
                Id = skillId,
                DisplayName = "Test Tool 2",
                Description = "A tool for testing proposal triggers."
            };
            _registry.RegisterSkill(skillRecord);
            await _registry.SaveAsync();

            // Seed some failure trajectories so the Miner has actual failures to build a proposal from
            var eventStore = new FileAgentEventStore(_tempWorkspace);
            await eventStore.AppendEventAsync(AppState.SessionId, new ToolCalledEvent
            {
                Version = 1,
                ToolUseId = "call-101",
                ToolName = skillId,
                Arguments = "{\"TargetPath\":\"error-path\"}"
            });
            await eventStore.AppendEventAsync(AppState.SessionId, new ToolResultEvent
            {
                Version = 2,
                ToolUseId = "call-101",
                Result = "NullReferenceException: Object reference not set to an instance of an object.",
                IsError = true
            });

            // Act: Record 3 failures (threshold is at least 3 total with >=50% failure rate)
            await recorder.RecordAsync(skillId, success: false, elapsedMs: 100, score: 0, error: "NullReferenceException");
            await recorder.RecordAsync(skillId, success: false, elapsedMs: 110, score: 0, error: "NullReferenceException");
            await recorder.RecordAsync(skillId, success: false, elapsedMs: 120, score: 0, error: "NullReferenceException");

            // Assert: verify proposal is generated automatically
            await _proposalService.LoadAsync(_tempWorkspace);
            var proposals = _proposalService.ListProposals();
            Assert.NotEmpty(proposals);

            var proposal = proposals.FirstOrDefault(p => p.Title.Contains(skillId) || p.Rationale.Contains(skillId));
            Assert.NotNull(proposal);
            Assert.Equal(SkillProposalStatus.Proposed, proposal.Status);
            Assert.Contains("NullReferenceException", proposal.Rationale);
        }
    }
}
