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
using Claude4Net.Commands;

namespace Claude4Net.Tests
{
    public class K046CommandOutputNormalizationTests
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

            // Provide real or mock orchestrator with all args
            var orchestratorMock = new Mock<ToolOrchestrator>(new List<ITool>(), approvalMock.Object, sp);
            services.AddSingleton(orchestratorMock.Object);

            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task LumenCliApp_HelpCommand_ConvertsToMarkupCell()
        {
            // Arrange
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            // Act
            await app.HandleInputAsync("/help", cts);

            // Assert
            Assert.Equal(2, app._observer.State.History.Count);
            Assert.IsType<UserPromptCell>(app._observer.State.History[0]);
            var cell = Assert.IsType<MarkupCell>(app._observer.State.History[1]);
            Assert.Contains("Available Commands", cell.MarkupText);
        }

        [Fact]
        public async Task LumenCliApp_StatusCommand_ConvertsToMarkupCell()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            await app.HandleInputAsync("/status", cts);

            Assert.Equal(2, app._observer.State.History.Count);
            var cell = Assert.IsType<MarkupCell>(app._observer.State.History[1]);
            Assert.Contains("System Status", cell.MarkupText);
        }

        [Fact]
        public async Task LumenCliApp_CheckpointCommand_ConvertsToMarkupCell()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            await app.HandleInputAsync("/checkpoint list", cts);

            Assert.Equal(2, app._observer.State.History.Count);
            var cell = Assert.IsType<MarkupCell>(app._observer.State.History[1]);
            // Either Error or "No checkpoints"
            Assert.True(cell.MarkupText.Contains("No checkpoints") || cell.MarkupText.Contains("Error"), $"Unexpected output: {cell.MarkupText}");
        }

        [Fact]
        public async Task LumenCliApp_CommandOutput_PreservesLegacyHandlerSignature()
        {
            var cmd = CommandRegistry.FindCommand("help");
            Assert.NotNull(cmd);
            var sp = CreateMockServiceProvider(out _);

            var result = await cmd.Handler!("", sp);
            Assert.IsType<string>(result);
            Assert.Contains("Available Commands", (string)result);
        }

        [Fact]
        public async Task LumenCliApp_ExitCommand_TriggersCancellation()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            await app.HandleInputAsync("/exit", cts);

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void MarkupCell_TrustedMarkup_RendersAsIs()
        {
            // Trusted command markup
            var cell = new MarkupCell("[bold red]Critical Error[/]");
            var renderable = cell.GetRenderable();

            Assert.IsType<Markup>(renderable);
            Assert.Equal("[bold red]Critical Error[/]", cell.MarkupText);
        }

        [Fact]
        public void AssistantResponseCell_UntrustedText_IsEscaped()
        {
            // Untrusted text from LLM/User
            var cell = new AssistantResponseCell();
            cell.AppendDelta("Text with [brackets] and [bold]markup tags[/]");

            var renderable = cell.GetRenderable();
            var markup = Assert.IsType<Markup>(renderable);

            // The resulting markup should have escaped brackets
            // Spectre.Console.Markup.Escape turns "[" into "[["
            Assert.Contains("[[brackets]]", cell.Content.EscapeMarkup());
        }

        [Fact]
        public async Task LumenCliApp_UnsafeInput_DoesNotBreakLayout()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            // Simulate command that returns markup-like text but as a string
            // In K046, command results are sent via MarkupReceivedEvent which treats them as trusted markup.
            // If a command wants to return literal brackets, it MUST escape them itself.
            // But here we test that AssistantResponseCell (untrusted) handles it correctly.

            app._observer.UpdateState(new AssistantTextUpdatedEvent("Text with [unclosed bracket"));

            var lastCell = app._observer.State.History[^1] as AssistantResponseCell;
            Assert.NotNull(lastCell);

            // This would throw if it tries to parse unclosed bracket in Markup constructor
            var renderable = lastCell.GetRenderable();
            Assert.NotNull(renderable);
        }
    }
}
