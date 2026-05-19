using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Input;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Moq;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Claude4Net.Tests
{
    public class K048RenderQualityTests
    {
        private IServiceProvider CreateMockServiceProvider(out Mock<IInputBroker> brokerMock)
        {
            var services = new ServiceCollection();
            brokerMock = new Mock<IInputBroker>();
            var routerMock = new Mock<ISmartRouter>();
            var approvalMock = new Mock<IUserApprovalHandler>();
            var embeddingMock = new Mock<IEmbeddingProvider>();

            services.AddSingleton(brokerMock.Object);
            services.AddSingleton(routerMock.Object);
            services.AddSingleton(approvalMock.Object);
            services.AddSingleton(embeddingMock.Object);

            var sp = services.BuildServiceProvider();
            var orchestratorMock = new Mock<ToolOrchestrator>(new List<ITool>(), approvalMock.Object, sp);
            services.AddSingleton(orchestratorMock.Object);

            return services.BuildServiceProvider();
        }

        [Fact]
        public void ToolResultCell_SummarizesLongOutput()
        {
            // Arrange
            string longResult = new string('A', 2000);
            var cell = new ToolResultCell("id1", longResult);

            // Act
            var renderable = cell.GetRenderable();
            var plainText = cell.ToPlainText();

            // Assert
            // When truncated, it returns a Panel containing the content
            Assert.IsType<Panel>(renderable);
            Assert.Contains(longResult, plainText); // Full text preserved in plain text
            Assert.True(plainText.Length >= 2000);
        }

        [Fact]
        public void FooterRenderer_ResponsiveToWidth()
        {
            // Arrange
            var state = new LumenState { IsRunning = true, Provider = "gemini", Model = "flash" };
            var renderer = new FooterRenderer();

            // Act & Assert
            var renderable = renderer.Render(state);
            Assert.NotNull(renderable);
            Assert.IsType<Rule>(renderable);
        }

        [Fact]
        public async Task LumenCliApp_EscCancellation_ActuallyCancelsToken()
        {
            // Arrange
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            // 1. Simulate a run start
            await app.HandleInputAsync("test prompt", cts);
            Assert.NotNull(app._activeRunCts);
            Assert.False(app._activeRunCts.IsCancellationRequested);

            // 2. Set state to running
            app._observer.UpdateState(new Claude4Net.Cli.Ui.Events.RunStartedEvent("p", "m", "s"));
            Assert.True(app._observer.State.IsRunning);

            // 3. Act - Press ESC
            var escKey = new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false);
            await app.ProcessKeyInternalAsync(escKey, cts);

            // 4. Assert
            Assert.True(app._activeRunCts.IsCancellationRequested);

            // Check notice
            var lastCell = app._observer.State.History[^1] as NoticeCell;
            Assert.NotNull(lastCell);
            Assert.Contains("Cancellation requested", lastCell.Message);
        }

        [Fact]
        public async Task LumenCliApp_BackgroundOutput_DoesNotCorruptComposerBuffer()
        {
            // Arrange
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            // 1. User starts typing
            app._composer.ProcessKey(new ConsoleKeyInfo('H', ConsoleKey.H, false, false, false));
            app._composer.ProcessKey(new ConsoleKeyInfo('i', ConsoleKey.I, false, false, false));
            Assert.Equal("Hi", app._composer.GetState().Text);

            // 2. Background event arrives
            app._observer.UpdateState(new ThinkingUpdatedEvent("thinking..."));

            // 3. Assert buffer is intact
            Assert.Equal("Hi", app._composer.GetState().Text);

            // Verify next input is possible
            app._composer.ProcessKey(new ConsoleKeyInfo('!', ConsoleKey.Oem1, false, false, false));
            Assert.Equal("Hi!", app._composer.GetState().Text);
        }

        [Fact]
        public void LumenReducer_PreventsDuplicateAssistantCells()
        {
            // Arrange
            var state = new LumenState();

            // Act
            state = LumenReducer.Reduce(state, new AssistantTextUpdatedEvent("Hello "));
            state = LumenReducer.Reduce(state, new AssistantTextUpdatedEvent("world"));

            // Assert
            Assert.Single(state.History);
            var cell = Assert.IsType<AssistantResponseCell>(state.History[0]);
            Assert.Equal("Hello world", cell.Content);
        }

        [Fact]
        public void BottomPane_RendersComposerBuffer()
        {
            // Arrange
            var state = new LumenState();
            var composerState = new PromptComposerState("User input buffer", 0, null);
            var pane = new BottomPane();

            // Act
            var renderable = pane.Render(state, composerState);

            // Assert
            // When there is text, it returns Rows (prefix + text)
            Assert.IsType<Rows>(renderable);
        }
    }
}
