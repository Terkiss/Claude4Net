using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K035 Agentic Search, Memory Strategy, and Audit Traceability 테스트
    /// </summary>
    public class K035MemoryAndAuditTests
    {
        // --- Memory Strategy Tests ---

        private List<ConversationMessage> CreateSampleHistory(int count)
        {
            var messages = new List<ConversationMessage>
            {
                new() { Role = "system", Content = "You are a helpful agent.", EstimatedTokens = 20 }
            };

            for (int i = 1; i <= count; i++)
            {
                messages.Add(new ConversationMessage
                {
                    Role = i % 2 == 1 ? "user" : "assistant",
                    Content = $"Message {i}",
                    Timestamp = DateTime.UtcNow.AddMinutes(-count + i),
                    EstimatedTokens = 50
                });
            }

            return messages;
        }

        [Fact]
        public void FullHistoryStrategy_RetainsAllMessages()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.FullHistory
            });

            var messages = CreateSampleHistory(30);
            var window = manager.Apply(messages);

            Assert.Equal(MemoryStrategyType.FullHistory, window.AppliedStrategy);
            Assert.Equal(messages.Count, window.RetainedCount);
            Assert.Equal(0, window.SummarizedCount);
        }

        [Fact]
        public void SlidingWindowStrategy_KeepsRecentMessages()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.SlidingWindow,
                WindowSize = 10
            });

            var messages = CreateSampleHistory(30);
            var window = manager.Apply(messages);

            Assert.Equal(MemoryStrategyType.SlidingWindow, window.AppliedStrategy);
            Assert.Equal(31, window.OriginalCount); // 1 system + 30 messages
            // 시스템 메시지(1) + 최근 10개
            Assert.Equal(11, window.RetainedCount);
        }

        [Fact]
        public void SlidingWindowStrategy_PreservesPinnedMessages()
        {
            var messages = new List<ConversationMessage>
            {
                new() { Role = "system", Content = "System", EstimatedTokens = 20 },
                new() { Role = "user", Content = "Important!", IsPinned = true, EstimatedTokens = 50 },
                new() { Role = "assistant", Content = "Old response", EstimatedTokens = 50 },
            };

            // 추가 메시지
            for (int i = 0; i < 20; i++)
            {
                messages.Add(new ConversationMessage
                {
                    Role = "user",
                    Content = $"Msg {i}",
                    EstimatedTokens = 50
                });
            }

            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.SlidingWindow,
                WindowSize = 5
            });

            var window = manager.Apply(messages);

            // 시스템(1) + 핀(1) + 최근 5 = 7
            Assert.Equal(7, window.RetainedCount);
            // 핀된 메시지가 포함되어 있는지 확인
            Assert.Contains(window.Messages, m => m.IsPinned);
        }

        [Fact]
        public void SummaryBasedStrategy_SummarizesOldMessages()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.SummaryBased,
                WindowSize = 5,
                SystemMessageReserve = 1
            });

            var messages = CreateSampleHistory(20);
            var window = manager.Apply(messages);

            Assert.Equal(MemoryStrategyType.SummaryBased, window.AppliedStrategy);
            Assert.True(window.SummarizedCount > 0);
            // 요약 메시지가 포함되어 있는지 확인
            Assert.Contains(window.Messages, m => m.Content.Contains("[이전 대화 요약"));
        }

        [Fact]
        public void SummaryBasedStrategy_SmallHistoryNotSummarized()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.SummaryBased,
                WindowSize = 50
            });

            var messages = CreateSampleHistory(5);
            var window = manager.Apply(messages);

            Assert.Equal(0, window.SummarizedCount);
            Assert.Equal(messages.Count, window.RetainedCount);
        }

        [Fact]
        public void MemoryStrategyManager_EmptyMessages()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults();
            var window = manager.Apply(new List<ConversationMessage>());

            Assert.Equal(0, window.RetainedCount);
            Assert.Equal(0, window.OriginalCount);
        }

        [Fact]
        public void MemoryStrategyManager_ConfigUpdate()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults(new MemoryConfig
            {
                Strategy = MemoryStrategyType.FullHistory
            });

            Assert.Equal(MemoryStrategyType.FullHistory, manager.Config.Strategy);

            manager.UpdateConfig(new MemoryConfig
            {
                Strategy = MemoryStrategyType.SlidingWindow,
                WindowSize = 5
            });

            Assert.Equal(MemoryStrategyType.SlidingWindow, manager.Config.Strategy);
        }

        [Fact]
        public void MemoryStrategyManager_DefaultStrategiesCount()
        {
            var manager = MemoryStrategyManager.CreateWithDefaults();
            Assert.Equal(3, manager.StrategyCount);
        }

        // --- Audit Trail Tests ---

        [Fact]
        public void AuditTrail_RecordAndRetrieve()
        {
            var audit = new AuditTrailService();
            audit.Record(AuditCategory.Routing, "Selected gemini-cli", "gemini-cli selected for DeepCode");

            Assert.Equal(1, audit.Count);
            var entries = audit.GetAll();
            Assert.Equal("Selected gemini-cli", entries[0].Action);
        }

        [Fact]
        public void AuditTrail_FilterByCategory()
        {
            var audit = new AuditTrailService();
            audit.Record(AuditCategory.Routing, "Route 1");
            audit.Record(AuditCategory.ToolExecution, "Tool 1");
            audit.Record(AuditCategory.Routing, "Route 2");
            audit.Record(AuditCategory.Security, "Security event");

            var routingEntries = audit.GetByCategory(AuditCategory.Routing);
            Assert.Equal(2, routingEntries.Count);
        }

        [Fact]
        public void AuditTrail_FilterBySeverity()
        {
            var audit = new AuditTrailService();
            audit.Record(AuditCategory.Security, "Normal access", severity: AuditSeverity.Info);
            audit.Record(AuditCategory.Security, "Suspicious path", severity: AuditSeverity.Warning);
            audit.Record(AuditCategory.Security, "Path traversal blocked", severity: AuditSeverity.Critical);

            var critical = audit.GetBySeverity(AuditSeverity.Critical);
            Assert.Single(critical);
            Assert.Contains("traversal", critical[0].Action);
        }

        [Fact]
        public void AuditTrail_FilterBySession()
        {
            var audit = new AuditTrailService();
            audit.Record(AuditCategory.Routing, "S1 Route", sessionId: "session-1");
            audit.Record(AuditCategory.Routing, "S2 Route", sessionId: "session-2");
            audit.Record(AuditCategory.ToolExecution, "S1 Tool", sessionId: "session-1");

            var session1 = audit.GetBySession("session-1");
            Assert.Equal(2, session1.Count);
        }

        [Fact]
        public void AuditTrail_FilterByTimeRange()
        {
            var audit = new AuditTrailService();
            var now = DateTime.UtcNow;

            audit.Record(new AuditEntry
            {
                Category = AuditCategory.Routing,
                Action = "Old",
                Timestamp = now.AddHours(-2)
            });
            audit.Record(new AuditEntry
            {
                Category = AuditCategory.Routing,
                Action = "Recent",
                Timestamp = now.AddMinutes(-5)
            });

            var recent = audit.GetByTimeRange(now.AddMinutes(-10), now);
            Assert.Single(recent);
            Assert.Equal("Recent", recent[0].Action);
        }

        [Fact]
        public void AuditTrail_CircularBuffer()
        {
            var audit = new AuditTrailService(maxEntries: 5);

            for (int i = 0; i < 10; i++)
            {
                audit.Record(AuditCategory.Routing, $"Entry {i}");
            }

            Assert.Equal(5, audit.Count);
            // 최신 5개만 남아야 함
            Assert.Equal("Entry 5", audit.GetAll()[0].Action);
            Assert.Equal("Entry 9", audit.GetAll()[4].Action);
        }

        [Fact]
        public void AuditTrail_ClearEntries()
        {
            var audit = new AuditTrailService();
            audit.Record(AuditCategory.Routing, "Test 1");
            audit.Record(AuditCategory.Routing, "Test 2");

            Assert.Equal(2, audit.Count);
            audit.Clear();
            Assert.Equal(0, audit.Count);
        }

        [Fact]
        public void AuditEntry_MetadataSupport()
        {
            var entry = new AuditEntry
            {
                Category = AuditCategory.Verification,
                Action = "Gate check",
                Metadata = { ["result"] = "pass", ["checks"] = "5" }
            };

            Assert.Equal("pass", entry.Metadata["result"]);
            Assert.NotEmpty(entry.Id);
        }
    }
}
