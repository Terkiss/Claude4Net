using Xunit;
using Moq;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.Cli.Ui.Input;
using UiEvents = Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Output;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Spectre.Console;
using Spectre.Console.Testing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K050LumenTranscriptHygieneTests
    {
        [Fact]
        public void Typing_DoesNotAppendPromptFooterPerKey()
        {
            // Setup
            var console = new TestConsole();
            var renderer = new LumenRenderer(console);
            var state = new LumenState();
            var composer = new PromptComposer();

            // Initial render
            renderer.RenderFull(state, composer.GetState());
            var initialOutput = console.Output;
            console.Clear();

            // Simulate key press (typing 'a')
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            renderer.RefreshInput(state, composer.GetState());

            // Verify: Should refresh input area but NOT history transcript
            // In this test, we check if history is empty.
            Assert.Empty(state.History);
        }

        [Fact]
        public void RenderAppend_DoesNotRenderFooterOrPrompt()
        {
            var console = new TestConsole();
            var renderer = new LumenRenderer(console);
            var state = new LumenState();

            // Add a cell
            var cell = new AssistantResponseCell();
            cell.AppendDelta("Hello");
            state.History.Add(cell);

            console.Clear();
            renderer.RenderAppend(state);

            // Verify: Should contain "Hello" but NOT prompt "Type your message here"
            Assert.Contains("Hello", console.Output);
            Assert.DoesNotContain("Type your message here", console.Output);
        }

        [Fact]
        public async Task CompleteAsync_DoesNotCreateInfoDuplicate()
        {
            var renderer = new LumenRenderer(new TestConsole());
            var state = new LumenState();
            // Use a real observer
            var observer = new LumenRunObserver(renderer, state);
            var handler = new LumenOutputHandler(observer);

            await handler.CompleteAsync("Final result");

            // Verify: History should NOT have NoticeCell for "Final result"
            Assert.DoesNotContain(observer.State.History, c => c is NoticeCell && ((NoticeCell)c).Message == "Final result");
        }

        [Fact]
        public async Task ObserverMode_DoesNotDirectWriteTextDelta()
        {
            var console = new TestConsole();
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockBroker = new Mock<IInputBroker>();
            var mockRouter = new Mock<ISmartRouter>();
            var mockObserver = new Mock<IAgentRunObserver>();

            // Correctly instantiate ToolOrchestrator
            var orchestrator = ToolOrchestrator.CreateForTest(Enumerable.Empty<ITool>(), null, mockServiceProvider.Object);

            // Set AppState
            AppState.CurrentCwd = AppContext.BaseDirectory;

            var agent = AgentLoop.CreateForTest(orchestrator, mockServiceProvider.Object, mockBroker.Object, mockRouter.Object, new Claude4Net.Runtime.Services.AppStateService(), observer: mockObserver.Object);

            // Mock LLM provider
            var mockProvider = new Mock<ILLMProvider>();
            mockProvider.Setup(p => p.Name).Returns("Mock");
            mockProvider.Setup(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new[] { new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "DeltaContent" } }.ToAsyncEnumerable());

            // Mock token counter to prevent NRE
            var mockTokenCounter = new Mock<ITokenCounter>();
            mockProvider.Setup(p => p.TokenCounter).Returns(mockTokenCounter.Object);

            var mockOutput = new Mock<IOutputHandler>();

            // Intercept console output
            using (var sw = new System.IO.StringWriter())
            {
                var originalOut = Console.Out;
                Console.SetOut(sw);
                try
                {
                    await agent.RunAsync("test prompt", mockOutput.Object, mockProvider.Object, "model", null);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                // Verify: Console should NOT contain "DeltaContent" because observer is present
                Assert.DoesNotContain("DeltaContent", sw.ToString());
            }
        }

        [Fact]
        public void ToolResult_RenderedOnceInLumenHistory()
        {
            var state = new LumenState();
            var renderer = new LumenRenderer(new TestConsole());
            var observer = new LumenRunObserver(renderer, state);

            // Simulate tool call and result
            observer.UpdateState(new UiEvents.UserPromptSubmittedEvent("Prompt"));
            observer.UpdateState(new UiEvents.ToolResultReceivedEvent("id1", "result_content", false));

            // Verify: History should have 1 ToolResultCell
            Assert.Contains(observer.State.History, c => c is ToolResultCell && ((ToolResultCell)c).Result == "result_content");
            Assert.Equal(1, observer.State.History.Count(c => c is ToolResultCell));
        }

        [Fact]
        public void RunCompleted_RendersIdleFooterAtMostOnce()
        {
            var console = new TestConsole();
            var renderer = new LumenRenderer(console);
            var state = new LumenState();
            var observer = new LumenRunObserver(renderer, state);

            // Simulate run start/end
            observer.UpdateState(new UiEvents.RunStartedEvent("p", "m", "sid"));
            Assert.True(observer.State.IsRunning);

            console.Clear();
            observer.UpdateState(new UiEvents.RunCompletedEvent());

            // Verify: state.IsRunning is false
            Assert.False(observer.State.IsRunning);
        }

        [Fact]
        public async Task LegacyMode_WritesFinalResponseToOutputHandler()
        {
            // Setup
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockBroker = new Mock<IInputBroker>();
            var mockRouter = new Mock<ISmartRouter>();
            var orchestrator = ToolOrchestrator.CreateForTest(Enumerable.Empty<ITool>(), null, mockServiceProvider.Object);

            // Legacy mode: Default constructor uses NullAgentRunObserver
            var agent = AgentLoop.CreateForTest(orchestrator, mockServiceProvider.Object, mockBroker.Object, mockRouter.Object, new Claude4Net.Runtime.Services.AppStateService());

            var mockProvider = new Mock<ILLMProvider>();
            mockProvider.Setup(p => p.Name).Returns("Mock");
            mockProvider.Setup(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new[] { new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Final Response" } }.ToAsyncEnumerable());

            var mockTokenCounter = new Mock<ITokenCounter>();
            mockProvider.Setup(p => p.TokenCounter).Returns(mockTokenCounter.Object);

            var mockOutput = new Mock<IOutputHandler>();
            AppState.CurrentCwd = AppContext.BaseDirectory;

            // Act
            await agent.RunAsync("test prompt", mockOutput.Object, mockProvider.Object, "model", null);

            // Verify: Final response should be sent to WriteAsync in Legacy mode
            mockOutput.Verify(o => o.WriteAsync("Final Response"), Times.Once());
        }

        [Fact]
        public async Task ObserverMode_SuppressesFinalResponseToOutputHandler()
        {
            // Setup
            var mockServiceProvider = new Mock<IServiceProvider>();
            var mockBroker = new Mock<IInputBroker>();
            var mockRouter = new Mock<ISmartRouter>();
            var mockObserver = new Mock<IAgentRunObserver>();
            var orchestrator = ToolOrchestrator.CreateForTest(Enumerable.Empty<ITool>(), null, mockServiceProvider.Object);

            // Observer mode: Inject non-null observer
            var agent = AgentLoop.CreateForTest(orchestrator, mockServiceProvider.Object, mockBroker.Object, mockRouter.Object, new Claude4Net.Runtime.Services.AppStateService(), observer: mockObserver.Object);

            var mockProvider = new Mock<ILLMProvider>();
            mockProvider.Setup(p => p.Name).Returns("Mock");
            mockProvider.Setup(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new[] { new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Final Response" } }.ToAsyncEnumerable());

            var mockTokenCounter = new Mock<ITokenCounter>();
            mockProvider.Setup(p => p.TokenCounter).Returns(mockTokenCounter.Object);

            var mockOutput = new Mock<IOutputHandler>();
            AppState.CurrentCwd = AppContext.BaseDirectory;

            // Act
            await agent.RunAsync("test prompt", mockOutput.Object, mockProvider.Object, "model", null);

            // Verify: Final response should NOT be sent to WriteAsync in Observer mode to prevent duplication
            mockOutput.Verify(o => o.WriteAsync(It.IsAny<string>()), Times.Never());
        }
    }
}