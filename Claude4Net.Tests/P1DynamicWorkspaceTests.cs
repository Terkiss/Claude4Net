using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class P1DynamicWorkspaceTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _ws1;
        private readonly string _ws2;
        private readonly string? _originalCwd;
        private readonly string _originalSessionId;

        public P1DynamicWorkspaceTests()
        {
            _originalCwd = AppState.CurrentCwd;
            _originalSessionId = AppState.SessionId;

            _tempBase = Path.Combine(Path.GetTempPath(), "Claude4Net_P1_" + Guid.NewGuid().ToString());
            _ws1 = Path.Combine(_tempBase, "Workspace1");
            _ws2 = Path.Combine(_tempBase, "Workspace2");
            Directory.CreateDirectory(_ws1);
            Directory.CreateDirectory(_ws2);
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.SessionId = _originalSessionId;

            if (Directory.Exists(_tempBase))
                Directory.Delete(_tempBase, true);
        }

        [Fact]
        public async Task AgentLoop_ShouldUseCurrentWorkspaceForEvents()
        {
            // Arrange
            var approvalHandler = new Mock<IUserApprovalHandler>().Object;
            var orchestratorServices = new ServiceCollection().BuildServiceProvider();
            var mockOrchestrator = new Mock<ToolOrchestrator>(new ITool[0], approvalHandler, orchestratorServices);
            var mockBroker = new Mock<IInputBroker>();
            var mockRouter = new Mock<ISmartRouter>();
            var services = new ServiceCollection();
            var serviceProvider = services.BuildServiceProvider();

            // Set initial workspace
            AppState.CurrentCwd = _ws1;
            AppState.SessionId = "test-session";

            var agent = new AgentLoop(
                mockOrchestrator.Object,
                serviceProvider,
                mockBroker.Object,
                mockRouter.Object);

            // Act 1: Run in Workspace 1
            var mockOutput = new Mock<IOutputHandler>();
            var mockProvider = new Mock<ILLMProvider>();
            mockProvider.Setup(p => p.Name).Returns("test-provider");
            mockProvider.Setup(p => p.TokenCounter).Returns(new DefaultTokenCounter());
            mockProvider.Setup(p => p.ContextLimit).Returns(100000);

            mockProvider.Setup(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                        .Returns(AsyncEnumerable.Empty<LLMStreamEvent>());

            await agent.RunAsync("Hello WS1", mockOutput.Object, mockProvider.Object, "model");

            // Verify event in WS1
            string eventPath1 = Path.Combine(_ws1, ".claude4net", "sessions", AppState.SessionId, "events.jsonl");
            Assert.True(File.Exists(eventPath1), $"Event file should exist in WS1: {eventPath1}");

            // Act 2: Switch Workspace
            AppState.CurrentCwd = _ws2;

            await agent.RunAsync("Hello WS2", mockOutput.Object, mockProvider.Object, "model");

            // Verify event in WS2
            string eventPath2 = Path.Combine(_ws2, ".claude4net", "sessions", AppState.SessionId, "events.jsonl");
            Assert.True(File.Exists(eventPath2), $"Event file should exist in WS2: {eventPath2}");
        }

        [Fact]
        public async Task AgentHub_GetInitialState_ShouldUseCurrentWorkspace()
        {
            // Arrange
            AppState.CurrentCwd = _ws1;
            AppState.SessionId = "hub-test";

            // Create some history in WS1
            var store1 = new FileAgentEventStore(_ws1);
            await store1.AppendEventAsync(AppState.SessionId, new UserPromptReceivedEvent { Version = 1, Prompt = "WS1 Event" });

            // Create some history in WS2
            var store2 = new FileAgentEventStore(_ws2);
            await store2.AppendEventAsync(AppState.SessionId, new UserPromptReceivedEvent { Version = 1, Prompt = "WS2 Event" });

            var hub = new Claude4Net.Dashboard.Hubs.AgentHub();

            // Act 1: Call in WS1
            var state1 = await hub.GetInitialState();

            // Assert 1
            Assert.Equal(_ws1, state1.Workspace);
            var events1 = state1.RecentEvents;
            Assert.Single(events1);
            Assert.Contains("WS1 Event", ((UserPromptReceivedEvent)events1[0]).Prompt);

            // Act 2: Switch to WS2 and call again
            AppState.CurrentCwd = _ws2;
            var state2 = await hub.GetInitialState();

            // Assert 2
            Assert.Equal(_ws2, state2.Workspace);
            var events2 = state2.RecentEvents;
            Assert.Single(events2);
            Assert.Contains("WS2 Event", ((UserPromptReceivedEvent)events2[0]).Prompt);
        }

        [Fact]
        public async Task InitialState_Serialization_ShouldIncludePayloadFields()
        {
            // Arrange
            AppState.CurrentCwd = _ws1;
            AppState.SessionId = "serial-test";
            var store = new FileAgentEventStore(_ws1);

            await store.AppendEventAsync(AppState.SessionId, new UserPromptReceivedEvent { Version = 1, Prompt = "Test Prompt" });
            await store.AppendEventAsync(AppState.SessionId, new AgentThoughtEvent { Version = 2, Thought = "Test Thought" });
            await store.AppendEventAsync(AppState.SessionId, new FinalResponseGeneratedEvent { Version = 3, Response = "Test Response" });

            var hub = new Claude4Net.Dashboard.Hubs.AgentHub();

            // Act
            var state = await hub.GetInitialState();
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            Console.WriteLine("DEBUG_JSON: " + json);

            // Assert
            // RecentEvents should be serialized with concrete fields because it's List<object>
            Assert.Contains("\"Prompt\":\"Test Prompt\"", json);
            Assert.Contains("\"Thought\":\"Test Thought\"", json);
            Assert.Contains("\"Response\":\"Test Response\"", json);
            Assert.Contains("\"EventType\":\"UserPromptReceived\"", json);
        }
    }
}
