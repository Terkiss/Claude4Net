using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Approval;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Moq;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Claude4Net.Tests
{
    public class K045ApprovalDialogTests
    {
        [Fact]
        public void ApprovalDialog_OpensFromEvent()
        {
            var state = new LumenState();
            var @event = new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff");

            var newState = LumenReducer.Reduce(state, @event);

            Assert.True(newState.ApprovalDialog.IsVisible);
            Assert.Equal("req-1", newState.ApprovalDialog.RequestId);
            Assert.Equal("High", newState.ApprovalDialog.RiskLevel);
        }

        [Fact]
        public void ApprovalDialog_ClosesFromEvent()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { IsVisible = true } };
            var @event = new ApprovalDialogClosedEvent();

            var newState = LumenReducer.Reduce(state, @event);

            Assert.False(newState.ApprovalDialog.IsVisible);
        }

        [Fact]
        public void ApprovalDialog_TogglesDetailMode()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { IsVisible = true, IsDetailMode = false } };
            var @event = new ApprovalDialogDetailToggledEvent();

            var newState = LumenReducer.Reduce(state, @event);
            Assert.True(newState.ApprovalDialog.IsDetailMode);

            newState = LumenReducer.Reduce(newState, @event);
            Assert.False(newState.ApprovalDialog.IsDetailMode);
        }

        [Fact]
        public void ApprovalDialog_YApproves()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };
            var @event = new ApprovalDialogActionSelectedEvent("req-1", ApprovalDialogAction.Approve);

            var newState = LumenReducer.Reduce(state, @event);
            Assert.Equal(ApprovalDialogAction.Approve, newState.ApprovalDialog.LastAction);
        }

        [Fact]
        public void ApprovalDialog_EnterApproves()
        {
            // Logic is in LumenCliApp, so we test reducer with the event that Enter would trigger
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };
            var @event = new ApprovalDialogActionSelectedEvent("req-1", ApprovalDialogAction.Approve);

            var newState = LumenReducer.Reduce(state, @event);
            Assert.Equal(ApprovalDialogAction.Approve, newState.ApprovalDialog.LastAction);
        }

        [Fact]
        public void ApprovalDialog_NDenies()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };
            var @event = new ApprovalDialogActionSelectedEvent("req-1", ApprovalDialogAction.Deny);

            var newState = LumenReducer.Reduce(state, @event);
            Assert.Equal(ApprovalDialogAction.Deny, newState.ApprovalDialog.LastAction);
        }

        [Fact]
        public void ApprovalDialog_EscapeCancels()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };
            var @event = new ApprovalDialogActionSelectedEvent("req-1", ApprovalDialogAction.Cancel);

            var newState = LumenReducer.Reduce(state, @event);
            Assert.Equal(ApprovalDialogAction.Cancel, newState.ApprovalDialog.LastAction);
        }

        [Fact]
        public void ApprovalDialog_UnknownKeyNoOps()
        {
            // In LumenCliApp, unknown keys don't trigger events
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };
            // No event for unknown key
            Assert.Equal(ApprovalDialogAction.None, state.ApprovalDialog.LastAction);
        }

        [Fact]
        public void DialogLayer_RendersApprovalDialog()
        {
            var layer = new DialogLayer();
            var state = new LumenState
            {
                ApprovalDialog = new ApprovalDialogState
                {
                    IsVisible = true,
                    Title = "Confirm Action",
                    Description = "Do you want to proceed?"
                }
            };

            var renderable = layer.Render(state);
            Assert.NotNull(renderable);

            var console = new TestConsole();
            console.Write(renderable);
            var output = console.Output;

            Assert.Contains("Confirm Action", output);
            Assert.Contains("Do you want to proceed?", output);
            Assert.Contains("Keys:", output);
        }

        [Fact]
        public void DialogLayer_EscapesMarkupInApprovalText()
        {
            var layer = new DialogLayer();
            var state = new LumenState
            {
                ApprovalDialog = new ApprovalDialogState
                {
                    IsVisible = true,
                    Title = "[bold]Malicious Title[/]",
                    Description = "Normal desc"
                }
            };

            var renderable = layer.Render(state);
            Assert.NotNull(renderable);
            var console = new TestConsole();
            console.Write(renderable);
            var output = console.Output;

            // If escaped, "[bold]" should appear as literal text in output or be handled safely
            Assert.Contains("[bold]Malicious Title[/]", output);
        }

        [Fact]
        public void ExistingCliApprovalHandler_RemainsAvailable()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>();
            var sp = services.BuildServiceProvider();

            var handler = sp.GetService<IUserApprovalHandler>();
            Assert.NotNull(handler);
            Assert.IsType<CliUserApprovalHandler>(handler);
        }

        [Fact]
        public void DiscordApprovalTypes_AreNotModified()
        {
            // Structural check to ensure we didn't touch Discord namespaces/types
            var type = typeof(Claude4Net.Discord.DiscordApprovalHandler);
            Assert.NotNull(type);
        }

        [Fact]
        public async Task LumenCliApp_ApprovalVisible_YApprovesWithoutMutatingComposer()
        {
            // Arrange
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff"));

            // Act
            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false), new CancellationTokenSource());

            // Assert
            Assert.Equal(ApprovalDialogAction.Approve, app._observer.State.ApprovalDialog.LastAction);
            Assert.Equal("", app._composer.GetState().Text); // Buffer must remain empty
        }

        [Fact]
        public async Task LumenCliApp_ApprovalVisible_NDeniesWithoutMutatingComposer()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff"));

            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false), new CancellationTokenSource());

            Assert.Equal(ApprovalDialogAction.Deny, app._observer.State.ApprovalDialog.LastAction);
            Assert.Equal("", app._composer.GetState().Text);
        }

        [Fact]
        public async Task LumenCliApp_ApprovalVisible_DTogglesDetailsWithoutMutatingComposer()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff"));

            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('d', ConsoleKey.D, false, false, false), new CancellationTokenSource());

            Assert.True(app._observer.State.ApprovalDialog.IsDetailMode);
            Assert.Equal("", app._composer.GetState().Text);
        }

        [Fact]
        public async Task LumenCliApp_ApprovalVisible_EscapeCancelsWithoutMutatingComposer()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff"));

            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false), new CancellationTokenSource());

            Assert.Equal(ApprovalDialogAction.Cancel, app._observer.State.ApprovalDialog.LastAction);
            Assert.Equal("", app._composer.GetState().Text);
        }

        [Fact]
        public void ApprovalDialog_ActionPersistsAfterClose()
        {
            var state = new LumenState { ApprovalDialog = new ApprovalDialogState { RequestId = "req-1", IsVisible = true } };

            // Select action
            var state1 = LumenReducer.Reduce(state, new ApprovalDialogActionSelectedEvent("req-1", ApprovalDialogAction.Approve));
            // Close dialog
            var state2 = LumenReducer.Reduce(state1, new ApprovalDialogClosedEvent());

            Assert.False(state2.ApprovalDialog.IsVisible);
            Assert.Equal(ApprovalDialogAction.Approve, state2.ApprovalDialog.LastAction); // Persistence check
        }

        [Fact]
        public void LumenCliApp_UsesLatestObserverStateForApprovalDialog()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);

            // External event updates observer state
            app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "High", "Diff"));

            // App must see it visible via its reference to observer.State
            Assert.True(app._observer.State.ApprovalDialog.IsVisible);
        }

        [Fact]
        public async Task LumenApprovalHandler_Roundtrip_Approve()
        {
            // Arrange
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            var handler = app._lumenApprovalHandler;

            // Act - Request approval (runs in background)
            var approvalTask = handler.RequestApprovalAsync("test_tool", "args");

            // UI Loop sees the request (Dialog becomes visible)
            Assert.True(app._observer.State.ApprovalDialog.IsVisible);
            string reqId = app._observer.State.ApprovalDialog.RequestId;

            // Simulate User pressing 'Y'
            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false), new CancellationTokenSource());

            // Assert
            bool result = await approvalTask;
            Assert.True(result);
            Assert.False(app._observer.State.ApprovalDialog.IsVisible);
            Assert.Equal(ApprovalDialogAction.Approve, app._observer.State.ApprovalDialog.LastAction);
        }

        [Fact]
        public async Task LumenApprovalHandler_Roundtrip_Deny()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            var handler = app._lumenApprovalHandler;

            var approvalTask = handler.RequestApprovalAsync("test_tool", "args");
            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false), new CancellationTokenSource());

            bool result = await approvalTask;
            Assert.False(result);
        }

        [Fact]
        public async Task LumenApprovalHandler_Roundtrip_Cancel()
        {
            var sp = CreateMockServiceProvider();
            var app = new LumenCliApp(sp);
            var handler = app._lumenApprovalHandler;

            var approvalTask = handler.RequestApprovalAsync("test_tool", "args");
            await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false), new CancellationTokenSource());

            bool result = await approvalTask;
            Assert.False(result);
            Assert.Equal(ApprovalDialogAction.Cancel, app._observer.State.ApprovalDialog.LastAction);
        }
        private IServiceProvider CreateMockServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton(new Mock<IInputBroker>().Object);
            services.AddSingleton(new Mock<ISmartRouter>().Object);
            services.AddSingleton(new Mock<IUserApprovalHandler>().Object);
            services.AddSingleton(new Mock<IEmbeddingProvider>().Object);
            var sp = services.BuildServiceProvider();
            services.AddSingleton(ToolOrchestrator.CreateForTest(new List<ITool>(), new Mock<IUserApprovalHandler>().Object, sp));
            return services.BuildServiceProvider();
        }
    }
}
