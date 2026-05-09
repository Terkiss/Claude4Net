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
    public class K024EventSourcedStateTests : IDisposable
    {
        private readonly string _tempPath;

        public K024EventSourcedStateTests()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempPath))
                Directory.Delete(_tempPath, true);
        }

        [Fact]
        public async Task EventStore_AppendAndRead()
        {
            var store = new FileAgentEventStore(_tempPath);
            string sessionId = "test-session";

            var ev1 = new SessionStartedEvent { Version = 1, Provider = "claude", Model = "3.5-sonnet" };
            var ev2 = new UserPromptReceivedEvent { Version = 2, Prompt = "Hello" };

            await store.AppendEventAsync(sessionId, ev1);
            await store.AppendEventAsync(sessionId, ev2);

            var events = (await store.GetEventsAsync(sessionId)).ToList();

            Assert.Equal(2, events.Count);
            Assert.IsType<SessionStartedEvent>(events[0]);
            Assert.IsType<UserPromptReceivedEvent>(events[1]);
            Assert.Equal("claude", ((SessionStartedEvent)events[0]).Provider);
            Assert.Equal("Hello", ((UserPromptReceivedEvent)events[1]).Prompt);
        }

        [Fact]
        public async Task Reconstruct_FromEvents()
        {
            var events = new List<IAgentEvent>
            {
                new SessionStartedEvent { Version = 1, Provider = "claude", Model = "3.5-sonnet" },
                new UserPromptReceivedEvent { Version = 2, Prompt = "Tell me a joke" },
                new FinalResponseGeneratedEvent { Version = 3, Response = "Why did the chicken cross the road?" }
            };

            var state = AgentStateReconstructor.Reconstruct(events);

            Assert.Equal(3, state.LastVersion);
            Assert.Equal("Tell me a joke", state.CurrentTask);
            Assert.Equal(2, state.History.Count); // User prompt + Assistant response

            // Check content using dynamic or reflection since we use anonymous objects in reconstruction
            var firstMsg = state.History[0];
            var secondMsg = state.History[1];

            Assert.Contains("user", firstMsg.ToString()!);
            Assert.Contains("Tell me a joke", firstMsg.ToString()!);
            Assert.Contains("assistant", secondMsg.ToString()!);
            Assert.Contains("chicken", secondMsg.ToString()!);
        }

        [Fact]
        public async Task Reconstruct_FromSnapshot()
        {
            var snapshot = new AgentStateSnapshot
            {
                SessionId = "test",
                LastVersion = 10,
                CurrentTask = "Initial task",
                History = new List<object> { new { role = "user", content = "Initial" } }
            };

            var events = new List<IAgentEvent>
            {
                new UserPromptReceivedEvent { Version = 11, Prompt = "Follow up" }
            };

            var state = AgentStateReconstructor.Reconstruct(events, snapshot);

            Assert.Equal(11, state.LastVersion);
            Assert.Equal("Follow up", state.CurrentTask);
            Assert.Equal(2, state.History.Count);
        }

        [Fact]
        public void SnapshotPolicy_Threshold()
        {
            var policy = new SnapshotPolicy(10);
            Assert.True(policy.ShouldTakeSnapshot(20, 0));
            Assert.False(policy.ShouldTakeSnapshot(15, 10));
            Assert.True(policy.ShouldTakeSnapshot(21, 10));
        }
    }
}
