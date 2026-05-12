using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K034 Event Store v2 and CQRS Projections 테스트
    /// 프로젝션 엔진, 세션 요약, 도구 사용 통계, v2 쿼리 메서드를 검증합니다.
    /// </summary>
    public class K034EventStoreV2Tests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FileAgentEventStore _store;

        public K034EventStoreV2Tests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "claude4net-k034-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
            _store = new FileAgentEventStore(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        private List<IAgentEvent> CreateSampleEvents()
        {
            long version = 0;
            return new List<IAgentEvent>
            {
                new SessionStartedEvent { Version = ++version, WorkspacePath = "/test", Provider = "gemini", Model = "gemini-2.0-flash" },
                new UserPromptReceivedEvent { Version = ++version, Prompt = "Fix the bug" },
                new AgentThoughtEvent { Version = ++version, Thought = "Analyzing..." },
                new ToolCalledEvent { Version = ++version, ToolUseId = "tc1", ToolName = "file_read", Arguments = "{}" },
                new ToolResultEvent { Version = ++version, ToolUseId = "tc1", Result = "file contents", IsError = false },
                new ToolCalledEvent { Version = ++version, ToolUseId = "tc2", ToolName = "file_write", Arguments = "{}" },
                new ToolResultEvent { Version = ++version, ToolUseId = "tc2", Result = "ok", IsError = false },
                new ToolCalledEvent { Version = ++version, ToolUseId = "tc3", ToolName = "file_read", Arguments = "{}" },
                new ToolResultEvent { Version = ++version, ToolUseId = "tc3", Result = "error", IsError = true },
                new FinalResponseGeneratedEvent { Version = ++version, Response = "Bug fixed!" },
                new UserPromptReceivedEvent { Version = ++version, Prompt = "Now add tests" },
                new ToolCalledEvent { Version = ++version, ToolUseId = "tc4", ToolName = "file_write", Arguments = "{}" },
                new ToolResultEvent { Version = ++version, ToolUseId = "tc4", Result = "ok", IsError = false },
                new FinalResponseGeneratedEvent { Version = ++version, Response = "Tests added!" }
            };
        }

        // --- 프로젝션 엔진 테스트 ---

        /// <summary>
        /// 프로젝션 엔진이 이벤트를 프로젝션에 올바르게 적용하는지 검증
        /// </summary>
        [Fact]
        public void ProjectionEngine_AppliesEventsToProjection()
        {
            var engine = new EventProjectionEngine(_store);
            var sessionProjection = new SessionSummaryProjection();
            engine.RegisterProjection(sessionProjection);

            engine.ApplyEvents(CreateSampleEvents());

            Assert.Equal(14, sessionProjection.Model.TotalEventCount);
            Assert.Equal(14, engine.LastProcessedVersion);
        }

        /// <summary>
        /// 세션 요약 프로젝션이 이벤트에서 정확하게 집계되는지 검증
        /// </summary>
        [Fact]
        public void SessionSummaryProjection_BuildsFromEvents()
        {
            var projection = new SessionSummaryProjection();
            foreach (var e in CreateSampleEvents()) projection.Apply(e);

            Assert.Equal(2, projection.Model.UserPromptCount);
            Assert.Equal(4, projection.Model.ToolCallCount);
            Assert.Equal(1, projection.Model.ToolErrorCount);
            Assert.Equal(2, projection.Model.FinalResponseCount);
            Assert.Equal("gemini", projection.Model.Provider);
            Assert.Equal("gemini-2.0-flash", projection.Model.Model);
            Assert.Equal("/test", projection.Model.WorkspacePath);
            Assert.NotNull(projection.Model.StartedAt);
        }

        /// <summary>
        /// 도구 사용 프로젝션이 도구별 호출 수를 정확히 집계하는지 검증
        /// </summary>
        [Fact]
        public void ToolUsageProjection_CountsToolCalls()
        {
            var projection = new ToolUsageProjection();
            foreach (var e in CreateSampleEvents()) projection.Apply(e);

            Assert.Equal(2, projection.ToolStats.Count); // file_read, file_write

            Assert.True(projection.ToolStats.ContainsKey("file_read"));
            Assert.Equal(2, projection.ToolStats["file_read"].CallCount);
            Assert.Equal(1, projection.ToolStats["file_read"].SuccessCount);
            Assert.Equal(1, projection.ToolStats["file_read"].ErrorCount);

            Assert.True(projection.ToolStats.ContainsKey("file_write"));
            Assert.Equal(2, projection.ToolStats["file_write"].CallCount);
            Assert.Equal(2, projection.ToolStats["file_write"].SuccessCount);
            Assert.Equal(0, projection.ToolStats["file_write"].ErrorCount);
        }

        /// <summary>
        /// 복수 프로젝션이 동시에 동작하는지 검증
        /// </summary>
        [Fact]
        public void ProjectionEngine_MultipleProjections()
        {
            var engine = new EventProjectionEngine(_store);
            engine.RegisterProjection(new SessionSummaryProjection());
            engine.RegisterProjection(new ToolUsageProjection());

            engine.ApplyEvents(CreateSampleEvents());

            var summary = engine.GetProjection<SessionSummaryProjection>();
            var toolUsage = engine.GetProjection<ToolUsageProjection>();

            Assert.NotNull(summary);
            Assert.NotNull(toolUsage);
            Assert.Equal(14, summary!.Model.TotalEventCount);
            Assert.Equal(2, toolUsage!.ToolStats.Count);
        }

        /// <summary>
        /// Reset 후 Rebuild가 정확하게 동작하는지 검증
        /// </summary>
        [Fact]
        public void ProjectionEngine_ResetClearsState()
        {
            var projection = new SessionSummaryProjection();
            foreach (var e in CreateSampleEvents()) projection.Apply(e);

            Assert.Equal(14, projection.Model.TotalEventCount);

            projection.Reset();
            Assert.Equal(0, projection.Model.TotalEventCount);
        }

        // --- Event Store v2 쿼리 테스트 ---

        /// <summary>
        /// 이벤트 수 조회가 정확한지 검증
        /// </summary>
        [Fact]
        public async Task EventStoreV2_GetEventCount()
        {
            string sessionId = "k034-count-test";

            // 3개 이벤트 저장
            await _store.AppendEventAsync(sessionId, new SessionStartedEvent { Version = 1, WorkspacePath = "/test" });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent { Version = 2, Prompt = "Hello" });
            await _store.AppendEventAsync(sessionId, new FinalResponseGeneratedEvent { Version = 3, Response = "World" });

            int count = await _store.GetEventCountAsync(sessionId);
            Assert.Equal(3, count);
        }

        /// <summary>
        /// 존재하지 않는 세션의 이벤트 수가 0인지 검증
        /// </summary>
        [Fact]
        public async Task EventStoreV2_EmptySessionReturnsZero()
        {
            int count = await _store.GetEventCountAsync("nonexistent-session");
            Assert.Equal(0, count);
        }

        /// <summary>
        /// 시간 범위 필터링이 정확한지 검증
        /// </summary>
        [Fact]
        public async Task EventStoreV2_TimeRangeFilter()
        {
            string sessionId = "k034-time-test";
            var now = DateTime.UtcNow;

            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent
            {
                Version = 1, Prompt = "Old", Timestamp = now.AddMinutes(-30)
            });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent
            {
                Version = 2, Prompt = "Recent", Timestamp = now.AddMinutes(-5)
            });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent
            {
                Version = 3, Prompt = "Future", Timestamp = now.AddMinutes(30)
            });

            // 최근 10분 내 이벤트만 조회
            var recent = await _store.GetEventsByTimeRangeAsync(sessionId, now.AddMinutes(-10), now);
            Assert.Single(recent);
        }

        /// <summary>
        /// 타입별 이벤트 필터링이 정확한지 검증
        /// </summary>
        [Fact]
        public async Task EventStoreV2_FilterByType()
        {
            string sessionId = "k034-type-test";

            await _store.AppendEventAsync(sessionId, new SessionStartedEvent { Version = 1, WorkspacePath = "/test" });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent { Version = 2, Prompt = "Hello" });
            await _store.AppendEventAsync(sessionId, new ToolCalledEvent { Version = 3, ToolUseId = "t1", ToolName = "read" });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent { Version = 4, Prompt = "World" });

            var prompts = await _store.GetEventsByTypeAsync<UserPromptReceivedEvent>(sessionId);
            Assert.Equal(2, prompts.Count());
        }

        /// <summary>
        /// VerificationCompletedEvent가 이벤트 스토어에 저장/로드되는지 검증
        /// </summary>
        [Fact]
        public async Task EventStoreV2_VerificationCompletedEventRoundtrip()
        {
            string sessionId = "k034-verify-test";

            var verifyEvent = new VerificationCompletedEvent
            {
                Version = 1,
                VerifierSessionId = "verify-123",
                GeneratorSessionId = "gen-456",
                Verdict = "Pass",
                PassedChecks = 3,
                TotalChecks = 3
            };

            await _store.AppendEventAsync(sessionId, verifyEvent);

            var events = await _store.GetEventsAsync(sessionId);
            var loaded = events.OfType<VerificationCompletedEvent>().FirstOrDefault();

            Assert.NotNull(loaded);
            Assert.Equal("verify-123", loaded!.VerifierSessionId);
            Assert.Equal("Pass", loaded.Verdict);
            Assert.Equal(3, loaded.PassedChecks);
        }

        /// <summary>
        /// 프로젝션 엔진의 Replay가 이벤트 스토어에서 이벤트를 읽어 적용하는지 검증
        /// </summary>
        [Fact]
        public async Task ProjectionEngine_ReplayFromEventStore()
        {
            string sessionId = "k034-replay-test";

            // 이벤트 저장
            await _store.AppendEventAsync(sessionId, new SessionStartedEvent { Version = 1, WorkspacePath = "/replay", Provider = "claude", Model = "claude-3" });
            await _store.AppendEventAsync(sessionId, new UserPromptReceivedEvent { Version = 2, Prompt = "Hello" });
            await _store.AppendEventAsync(sessionId, new ToolCalledEvent { Version = 3, ToolUseId = "t1", ToolName = "bash" });
            await _store.AppendEventAsync(sessionId, new ToolResultEvent { Version = 4, ToolUseId = "t1", Result = "ok", IsError = false });

            // 프로젝션 엔진으로 재생
            var engine = new EventProjectionEngine(_store);
            engine.RegisterProjection(new SessionSummaryProjection());
            engine.RegisterProjection(new ToolUsageProjection());

            await engine.ReplayAsync(sessionId);

            var summary = engine.GetProjection<SessionSummaryProjection>();
            Assert.NotNull(summary);
            Assert.Equal(4, summary!.Model.TotalEventCount);
            Assert.Equal("claude", summary.Model.Provider);

            var tools = engine.GetProjection<ToolUsageProjection>();
            Assert.NotNull(tools);
            Assert.True(tools!.ToolStats.ContainsKey("bash"));
        }

        /// <summary>
        /// GetProjection으로 등록하지 않은 프로젝션을 조회하면 null 반환
        /// </summary>
        [Fact]
        public void ProjectionEngine_GetUnregisteredProjectionReturnsNull()
        {
            var engine = new EventProjectionEngine(_store);
            engine.RegisterProjection(new SessionSummaryProjection());

            var toolUsage = engine.GetProjection<ToolUsageProjection>();
            Assert.Null(toolUsage);
        }

        /// <summary>
        /// 빈 이벤트에 대한 프로젝션 적용이 정상 동작하는지 검증
        /// </summary>
        [Fact]
        public void ProjectionEngine_EmptyEventsNoError()
        {
            var engine = new EventProjectionEngine(_store);
            engine.RegisterProjection(new SessionSummaryProjection());

            engine.ApplyEvents(new List<IAgentEvent>());

            var summary = engine.GetProjection<SessionSummaryProjection>();
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Model.TotalEventCount);
        }
    }
}
