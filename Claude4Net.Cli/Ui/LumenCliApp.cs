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
using Claude4Net.Runtime.Services;
using Claude4Net.Dashboard;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Rendering;

using Claude4Net.Commands;
using Claude4Net.Cli.Ui.Rendering.HistoryCells;

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
        internal CancellationTokenSource? _activeRunCts;

        public LumenCliApp(IServiceProvider serviceProvider)

        {
            _serviceProvider = serviceProvider;
            _renderer = new LumenRenderer(
                AnsiConsole.Console,
                _serviceProvider.GetService<ILumenFrameBuilder>(),
                _serviceProvider.GetService<ILumenTerminalRenderer>());

            // Enable advanced frame-based rendering for Lumen mode
            _renderer.EnableLumenMode();

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
                _renderer.Shutdown();
                await consumerTask;
            }
        }

        internal async Task ProcessKeyInternalAsync(ConsoleKeyInfo keyInfo, CancellationTokenSource cts)
        {
            var currentState = _observer.State;

            if (keyInfo.Key == ConsoleKey.T && string.IsNullOrEmpty(_composer.GetState().Text))
            {
                for (int i = currentState.History.Count - 1; i >= 0; i--)
                {
                    if (currentState.History[i] is ThinkingCell thinkingCell)
                    {
                        thinkingCell.IsExpanded = !thinkingCell.IsExpanded;
                        _renderer.RenderFull(_observer.State, _composer.GetState());
                        return;
                    }
                    if (currentState.History[i] is ToolCallCell toolCallCell)
                    {
                        toolCallCell.IsExpanded = !toolCallCell.IsExpanded;
                        _renderer.RenderFull(_observer.State, _composer.GetState());
                        return;
                    }
                    if (currentState.History[i] is ToolResultCell toolResultCell)
                    {
                        toolResultCell.IsExpanded = !toolResultCell.IsExpanded;
                        _renderer.RenderFull(_observer.State, _composer.GetState());
                        return;
                    }
                }
            }

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

            _observer.State.IsCommandPaletteVisible = _composer.IsCommandPaletteVisible;
            _observer.State.PaletteFilterText = _composer.PaletteFilterText;
            _observer.State.PaletteSelectedIndex = _composer.PaletteSelectedIndex;

            switch (result.Status)
            {
                case PromptComposerStatus.Submitted:
                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        await HandleInputAsync(result.Text, cts);
                        // After submission, we want a fresh input area rendered ONCE
                        _renderer.RefreshInput(_observer.State, _composer.GetState());
                    }
                    break;

                case PromptComposerStatus.Cancelled:
                    if (_observer.State.IsRunning)
                    {
                        _activeRunCts?.Cancel();
                        _observer.UpdateState(new NoticeReceivedEvent("Cancellation requested via ESC.", "Warning"));
                    }
                    _renderer.RefreshInput(_observer.State, _composer.GetState());
                    break;

                case PromptComposerStatus.ClearSignal:
                    Console.Clear();
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    break;

                case PromptComposerStatus.Scrolled:
                    switch (result.Action)
                    {
                        case InputAction.ScrollUp:
                            _observer.UpdateState(new ScrollUpRequestedEvent(10));
                            break;
                        case InputAction.ScrollDown:
                            _observer.UpdateState(new ScrollDownRequestedEvent(10));
                            break;
                        case InputAction.ScrollToHome:
                            _observer.UpdateState(new ScrollToHomeRequestedEvent());
                            break;
                        case InputAction.ScrollToEnd:
                            _observer.UpdateState(new ScrollToEndRequestedEvent());
                            break;
                    }
                    _renderer.RefreshInput(_observer.State, _composer.GetState());
                    break;

                default:
                    // Regular typing:
                    // In Lumen mode, we want to refresh to show the characters being typed
                    if (_renderer.IsLumenMode)
                    {
                        _renderer.RefreshInput(_observer.State, _composer.GetState());
                    }
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
                string cmdName = parts[0].TrimStart('!', '/');
                string cmdArgs = parts.Length > 1 ? parts[1] : "";

                if (cmdName.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    _observer.UpdateState(new ClearTranscriptEvent());
                    Console.Clear();
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    return;
                }

                if (cmdName.Equals("theme", StringComparison.OrdinalIgnoreCase))
                {
                    string targetTheme = cmdArgs.Trim();
                    if (string.IsNullOrEmpty(targetTheme))
                    {
                        string current = LumenTheme.UserColor == "springgreen1" ? "neon" : (LumenTheme.UserColor == "darkgreen" ? "light" : "dark");
                        targetTheme = current == "dark" ? "neon" : (current == "neon" ? "light" : "dark");
                    }
                    _observer.UpdateState(new ThemeChangedEvent(targetTheme));
                    _observer.UpdateState(new NoticeReceivedEvent($"Theme switched to '{targetTheme}'", "Info"));
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    return;
                }

                if (cmdName.Equals("model", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(cmdArgs))
                    {
                        var cmdHelp = CommandRegistry.FindCommand("model");
                        if (cmdHelp != null && cmdHelp.Handler != null)
                        {
                            var resHelp = await cmdHelp.Handler("", _serviceProvider);
                            _observer.UpdateState(new MarkupReceivedEvent(resHelp));
                        }
                    }
                    else
                    {
                        var cmdModel = CommandRegistry.FindCommand("model");
                        if (cmdModel != null && cmdModel.Handler != null)
                        {
                            var resModel = await cmdModel.Handler(cmdArgs, _serviceProvider);
                            _observer.UpdateState(new ModelChangedEvent(AppState.ActiveProvider, AppState.ActiveModel));
                            _observer.UpdateState(new MarkupReceivedEvent(resModel));
                        }
                    }
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    return;
                }

                if (cmdName.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("[bold cyan]Lumen TUI Interface Help & Key Bindings:[/]");
                    sb.AppendLine("  [bold]Key Bindings:[/]");
                    sb.AppendLine("    [bold]PageUp/PageDown[/]  - Scroll transcript view up / down");
                    sb.AppendLine("    [bold]Home/End[/]          - Scroll to the top / bottom");
                    sb.AppendLine("    [bold]T[/]                 - Toggle collapse/expand on latest Thought/Tool block");
                    sb.AppendLine("    [bold]Esc[/]               - Cancel active assistant generation");
                    sb.AppendLine();
                    sb.AppendLine("  [bold]Available Commands & Slash Commands:[/]");
                    sb.AppendLine("    [bold]/help[/]            - Show this TUI help overview");
                    sb.AppendLine("    [bold]/clear[/]           - Reset rendering state and clear message history");
                    sb.AppendLine("    [bold]/theme <name>[/]    - Switch TUI themes (neon, light, dark)");
                    sb.AppendLine("    [bold]/model <name>[/]    - Show models list or switch active LLM config");
                    sb.AppendLine();
                    sb.AppendLine("[grey]To run standard tool commands, prefix with slash (e.g. /doctor, /skills, /pwd)[/]");
                    _observer.UpdateState(new MarkupReceivedEvent(sb.ToString()));
                    _renderer.RenderFull(_observer.State, _composer.GetState());
                    return;
                }

                var cmd = CommandRegistry.FindCommand(cmdName);
                if (cmd != null && cmd.Handler != null)
                {
                    try
                    {
                        var res = await cmd.Handler(cmdArgs, _serviceProvider);
                        _observer.UpdateState(new MarkupReceivedEvent(res));

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

            // Create run-specific cancellation token linked to app token
            _activeRunCts?.Dispose();
            _activeRunCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);

            // Route and Queue input for AgentLoop with Lumen-specific approval handler and run-specific token
            _broker.TryWrite(new InputContext(input, _outputHandler, _lumenApprovalHandler, _activeRunCts.Token));
        }

        private async Task StartConsumerAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var broadcaster = DashboardServer.Services?.GetService<IAgentEventBroadcaster>();
                
                // DI에서 AgentLoop를 생성하되, 현재 인스턴스의 _observer를 수동으로 설정하거나
                // 매번 새로운 인스턴스를 만들 때 필요한 서비스들을 직접 생성자에 넘깁니다.
                // LumenUI 전용 옵저버를 넘겨야 하므로 여기서는 수동 주입 방식을 사용합니다.
                var agent = new AgentLoop(
                    _serviceProvider.GetRequiredService<ToolOrchestrator>(),
                    _serviceProvider,
                    _broker,
                    _router,
                    _serviceProvider.GetRequiredService<RAGService>(),
                    _serviceProvider.GetRequiredService<TelemetryService>(),
                    _serviceProvider.GetRequiredService<Claude4Net.Runtime.Services.ISelfHealingService>(),
                    _serviceProvider.GetRequiredService<Claude4Net.SDK.IAppState>(),
                    _serviceProvider.GetService<IEmbeddingProvider>(),
                    broadcaster,
                    _observer);

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
