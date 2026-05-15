using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.Api;
using System.Linq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K039AgentRunObserverTests
    {
        [Fact]
        public async Task AgentLoop_ShouldReportEventsToObserver()
        {
            // Arrange
            AppState.SessionId = "test-session-k039-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            AppState.CurrentCwd = System.IO.Directory.GetCurrentDirectory();
            
            var observerMock = new Mock<IAgentRunObserver>();
            var events = new List<IAgentRunEvent>();
            observerMock.Setup(x => x.OnEventAsync(It.IsAny<IAgentRunEvent>()))
                .Callback<IAgentRunEvent>(e => events.Add(e))
                .Returns(Task.CompletedTask);

            // Mock dependencies for AgentLoop
            var serviceProviderMock = new Mock<IServiceProvider>();
            var orchestratorMock = new Mock<ToolOrchestrator>(new List<ITool>(), null!, serviceProviderMock.Object);
            var routerMock = new Mock<ISmartRouter>();
            var brokerMock = new Mock<IInputBroker>();
            
            var providerMock = new Mock<ILLMProvider>();
            providerMock.Setup(p => p.Name).Returns("test-provider");
            providerMock.Setup(p => p.TokenCounter).Returns(new DefaultTokenCounter());
            providerMock.Setup(p => p.ContextLimit).Returns(1000);
            providerMock.Setup(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<System.Threading.CancellationToken>()))
                .Returns(GetTestStream());

            routerMock.Setup(r => r.Route(It.IsAny<string>())).Returns(new RoutingDecision { SelectedProvider = "test-provider", SelectedModel = "test-model", Reason = "Mock reason" });
            
            // Set up ServiceProvider to return our provider mock
            serviceProviderMock.Setup(s => s.GetService(typeof(GeminiProvider))).Returns(providerMock.Object);
            serviceProviderMock.Setup(s => s.GetService(typeof(GeminiCliProvider))).Returns(providerMock.Object);
            serviceProviderMock.Setup(s => s.GetService(typeof(OllamaProvider))).Returns(providerMock.Object);
            serviceProviderMock.Setup(s => s.GetService(typeof(ClaudeService))).Returns(providerMock.Object);

            var loop = new AgentLoop(orchestratorMock.Object, serviceProviderMock.Object, brokerMock.Object, routerMock.Object, observer: observerMock.Object);

            var outputMock = new Mock<IOutputHandler>();
            
            // Act
            // Note: ListenAsync calls RunAsync internally, but we can call RunAsync directly for isolated testing of the observer.
            await loop.RunAsync("Hello", outputMock.Object, providerMock.Object, "test-model");

            // Assert
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e is RunStartedEvent);
            Assert.Contains(events, e => e is ThinkingStartedEvent);
            Assert.Contains(events, e => e is TextDeltaEvent);
            Assert.Contains(events, e => e is AssistantMessageCompletedEvent);
            Assert.Contains(events, e => e is RunCompletedEvent);
            
            var startEvent = events.OfType<RunStartedEvent>().First();
            Assert.Equal("test-provider", startEvent.Provider);
            Assert.Equal("Hello", startEvent.Prompt);
            
            var completedEvent = events.OfType<AssistantMessageCompletedEvent>().First();
            Assert.Equal("Hello world", completedEvent.FullResponse);
        }

        private async IAsyncEnumerable<LLMStreamEvent> GetTestStream()
        {
            yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Hello " };
            yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "world" };
            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = new LLMResponse { Text = "Hello world" } };
            await Task.Yield();
        }
    }
}
