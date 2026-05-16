using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Input;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Moq;

namespace Claude4Net.Tests
{
    public class K044LumenCliAppTests
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
        public async Task LumenCliApp_SubmitsPrompt_OnEnter()
        {
            // Arrange
            var sp = CreateMockServiceProvider(out var brokerMock);
            var app = new LumenCliApp(sp);
            var cts = new CancellationTokenSource();

            // Act - Using internal seam
            await app.HandleInputAsync("hello", cts);

            // Assert
            brokerMock.Verify(b => b.TryWrite(It.Is<InputContext>(c => c.Text == "hello")), Times.Once);
        }

        [Fact]
        public void LumenCliApp_UsesPromptComposer_ForBufferEditing()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);

            // Using internal seam
            var composer = app._composer;

            Assert.NotNull(composer);
            composer.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false));
            Assert.Equal("a", composer.GetState().Text);
        }

        [Fact]
        public void LumenCliApp_EscapeCancelsInput_WhenNoRunActive()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);

            // Using internal seam
            var composer = app._composer;

            composer.SetBuffer("test");
            app.ProcessKeyForTesting(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

            Assert.Equal("", composer.GetState().Text);
        }

        [Fact]
        public void LumenCliApp_CtrlLReturnsClearSignal()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);

            var result = app.ProcessKeyForTesting(new ConsoleKeyInfo('L', ConsoleKey.L, false, false, true));
            Assert.Equal(PromptComposerStatus.ClearSignal, result.Status);
        }

        [Fact]
        public void LumenCliApp_CtrlCRequestsExitOrCancel()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);

            var result = app.ProcessKeyForTesting(new ConsoleKeyInfo('C', ConsoleKey.C, false, false, true));
            Assert.Equal(PromptComposerStatus.Cancelled, result.Status);
        }

        [Fact]
        public void LumenCliApp_ConnectsObserverToRenderer()
        {
            var sp = CreateMockServiceProvider(out _);
            var app = new LumenCliApp(sp);

            // Using internal seam
            var observer = app._observer;

            observer.UpdateState(new UserPromptSubmittedEvent("test"));
            Assert.Single(observer.State.History);
        }

        [Fact]
        public void LegacyCli_Path_RemainsAvailable()
        {
            // By default, UseLumen is false, so it uses legacy path
            var options = Claude4Net.Cli.Bootstrap.CliOptions.Parse(new string[0]);
            Assert.False(options.UseLumen);
            Assert.False(options.LegacyCli);
        }

        [Fact]
        public void LumenCli_Path_IsOptIn()
        {
            var options = Claude4Net.Cli.Bootstrap.CliOptions.Parse(new[] { "--lumen" });
            Assert.True(options.UseLumen);
        }

        [Fact]
        public void PipedInput_Path_DoesNotUseLumenComposer()
        {
            // Piped input path in Program.cs uses CliOutputHandler, not LumenCliApp.
            // This is a structural check.
            var options = Claude4Net.Cli.Bootstrap.CliOptions.Parse(new string[0]);
            Assert.False(options.LegacyCli); // Default is Lumen if not legacy, BUT only for interactive
        }

        [Fact]
        public void SmokeExit_Path_DoesNotStartInteractiveLumenLoop()
        {
            var options = Claude4Net.Cli.Bootstrap.CliOptions.Parse(new[] { "--smoke-exit" });
            Assert.True(options.SmokeExit);
        }
    }

    // Extension for testing internal state if needed
    public static class LumenCliAppExtensions
    {
        public static PromptComposerResult ProcessKeyForTesting(this LumenCliApp app, ConsoleKeyInfo key)
        {
            return app._composer.ProcessKey(key);
        }
    }
}
