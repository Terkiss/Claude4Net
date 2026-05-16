using System;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui.Input;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Output;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Approval;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Dashboard;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

using Claude4Net.Commands;

namespace Claude4Net.Cli.Ui
{
    /// <summary>
    /// Interactive CLI application loop using Lumen UI components.
    /// </summary>
    public sealed class LumenCliApp
    {
        private readonly IServiceProvider _serviceProvider;
        internal readonly PromptComposer _composer = new();
        private readonly LumenRenderer _renderer;
        internal readonly LumenRunObserver _observer;
        private readonly LumenOutputHandler _outputHandler;
        private readonly IInputBroker _broker;
        private readonly ISmartRouter _router;
        private readonly ApprovalQueue _approvalQueue = new();
        internal readonly LumenApprovalHandler _lumenApprovalHandler;

        public LumenCliApp(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _renderer = new LumenRenderer(AnsiConsole.Console);
            var initialState = new LumenState();
            _observer = new LumenRunObserver(_renderer, initialState);
            _outputHandler = new LumenOutputHandler(_observer);
            _broker = _serviceProvider.GetRequiredService<IInputBroker>();
            _router = _serviceProvider.GetRequiredService<ISmartRouter>();
            _lumenApprovalHandler = new LumenApprovalHandler(_observer, _approvalQueue);
        }

        /// <summary>
        /// Starts the interactive Lumen CLI loop.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Start Consumer loop in background
            var consumerTask = StartConsumerAsync(cts.Token);

            // Initial Draw
            _renderer.RenderFull(_observer.State, _composer.GetState());

            // Main UI Loop (Producer)
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(true);
                        await ProcessKeyInternalAsync(keyInfo, cts);
                    }

                    await Task.Delay(20, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                _approvalQueue.CancelAll();
                cts.Cancel();
                await consumerTask;
            }
        }

        internal async Task ProcessKeyInternalAsync(ConsoleKeyInfo keyInfo, CancellationTokenSource cts)
        {
            var currentState = _observer.State;

            // P1 Fix: Priority check for Approval Dialog before Composer
            if (currentState.ApprovalDialog.IsVisible)
            {
                string reqId = currentState.ApprovalDialog.RequestId;
                switch (keyInfo.Key)
                {
                    case ConsoleKey.Y:
                    case ConsoleKey.Enter:
                        _observer.UpdateState(new ApprovalDialogActionSelectedEvent(reqId, ApprovalDialogAction.Approve));
                        _observer.UpdateState(new ApprovalDialogClosedEvent());
                        _approvalQueue.Resolve(reqId, ApprovalDialogAction.Approve);
                        break;
                    case ConsoleKey.N:
                        _observer.UpdateState(new ApprovalDialogActionSelectedEvent(reqId, ApprovalDialogAction.Deny));
                        _observer.UpdateState(new ApprovalDialogClosedEvent());
                        _approvalQueue.Resolve(reqId, ApprovalDialogAction.Deny);
                        break;
                    case ConsoleKey.D:
                        _observer.UpdateState(new ApprovalDialogDetailToggledEvent());
                        break;
                    case ConsoleKey.Escape:
                        _observer.UpdateState(new ApprovalDialogActionSelectedEvent(reqId, ApprovalDialogAction.Cancel));
                        _observer.UpdateState(new ApprovalDialogClosedEvent());
                        _approvalQueue.Resolve(reqId, ApprovalDialogAction.Cancel);
                        break;
                    default:
                        // P1 Fix: Unknown keys in dialog mode are NoOps
                        break;
                }

                // Re-render UI to reflect dialog changes or NoOp
                _renderer.RefreshInput(_observer.State, _composer.GetState());
                return;
            }

            // Normal composer processing
            var result = _composer.ProcessKey(keyInfo);

            // After processing key, refresh the input area
            _renderer.RefreshInput(_observer.State, _composer.GetState());

            switch (result.Status)
            {
                case PromptComposerStatus.Submitted:
                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        await HandleInputAsync(result.Text, cts);
                    }
                    break;

                case PromptComposerStatus.Cancelled:
                    if (_observer.State.IsRunning)
                    {
                        _observer.UpdateState(new NoticeReceivedEvent("Cancellation requested via ESC.", "Warning"));
                    }
                    break;

                case PromptComposerStatus.ClearSignal:
                    Console.Clear();
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    break;
            }
        }

        internal async Task HandleInputAsync(string input, CancellationTokenSource cts)
        {
            // Record user prompt in state
            _observer.UpdateState(new UserPromptSubmittedEvent(input));

            // Check for commands
            if (input.StartsWith("!") || input.StartsWith("/"))
            {
                string[] parts = input.Split(' ', 2);
                string cmdName = parts[0];
                string cmdArgs = parts.Length > 1 ? parts[1] : "";

                var cmd = CommandRegistry.FindCommand(cmdName);
                if (cmd != null && cmd.Handler != null)
                {
                    try
                    {
                        var res = await cmd.Handler(cmdArgs, _serviceProvider);
                        _observer.UpdateState(new NoticeReceivedEvent(res));
                        if (cmd.Name == "exit")
                        {
                            cts.Cancel();
                        }
                    }
                    catch (Exception ex)
                    {
                        _observer.UpdateState(new ErrorReceivedEvent($"Command execution failed: {ex.Message}"));
                    }
                    return;
                }
            }

            // Route and Queue input for AgentLoop with Lumen-specific approval handler
            _broker.TryWrite(new InputContext(input, _outputHandler, _lumenApprovalHandler));
        }

        private async Task StartConsumerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var broadcaster = DashboardServer.Services?.GetService<IAgentEventBroadcaster>();
                var agent = new AgentLoop(
                    _serviceProvider.GetRequiredService<ToolOrchestrator>(),
                    _serviceProvider,
                    _broker,
                    _router,
                    _serviceProvider.GetRequiredService<IEmbeddingProvider>(),
                    broadcaster,
                    _observer); // Constructor injection preferred over SetObserver

                try
                {
                    await agent.ListenAsync(token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _observer.UpdateState(new ErrorReceivedEvent($"Agent loop error: {ex.Message}"));
                    await Task.Delay(1000, token); // Prevent tight error loop
                }
            }
        }
    }
}
