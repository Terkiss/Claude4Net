using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Commands;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Claude4Net.Tests
{
    public class DummyTool : ITool
    {
        public string Name => "dummy_tool";
        public string Description => "A dummy tool for testing";
        public object? InputSchema => new { };

        public Task<object> ExecuteAsync(string input, object context, CancellationToken ct = default)
        {
            return Task.FromResult<object>("dummy_result");
        }
    }

    public class ErrorDummyTool : ITool
    {
        public string Name => "error_dummy_tool";
        public string Description => "A dummy tool that throws error";
        public object? InputSchema => new { };

        public Task<object> ExecuteAsync(string input, object context, CancellationToken ct = default)
        {
            throw new Exception("Intentional error for audit");
        }
    }

    public class DummyHookBefore : IToolHook
    {
        public string Name => "DummyHookBefore";
        public HookTiming Timing => HookTiming.BeforeToolExecution;
        public int Priority => 1;
        public bool IsEnabled { get; set; } = true;

        public bool ExecutedBefore { get; private set; }

        public Task<HookResult> ExecuteAsync(HookContext context)
        {
            ExecutedBefore = true;
            return Task.FromResult(HookResult.Ok(Name));
        }
    }

    public class DummyHookAfter : IToolHook
    {
        public string Name => "DummyHookAfter";
        public HookTiming Timing => HookTiming.AfterToolExecution;
        public int Priority => 1;
        public bool IsEnabled { get; set; } = true;

        public bool ExecutedAfter { get; private set; }

        public Task<HookResult> ExecuteAsync(HookContext context)
        {
            ExecutedAfter = true;
            return Task.FromResult(HookResult.Ok(Name));
        }
    }

    public class DummyBroker : IInputBroker
    {
        private readonly Queue<string> _inputs;

        public DummyBroker(params string[] inputs)
        {
            _inputs = new Queue<string>(inputs);
        }

        public ValueTask<InputContext> ReadAsync(CancellationToken ct = default)
        {
            if (_inputs.Count > 0)
            {
                var ctx = new InputContext(_inputs.Dequeue(), new Mock<IOutputHandler>().Object, new Mock<IUserApprovalHandler>().Object);
                return new ValueTask<InputContext>(ctx);
            }
            throw new OperationCanceledException();
        }

        public bool TryWrite(InputContext context) => true;
    }

    [Collection("AppState")]
    public class P1FixRegressionTests : IDisposable
    {
        private readonly string? _originalCwd;
        private readonly string _originalSessionId;
        private readonly string _originalActiveProvider;
        private readonly string _originalActiveModel;
        private readonly PermissionMode _originalPermissionMode;
        private readonly bool _originalIsProviderExplicitlySet;

        public P1FixRegressionTests()
        {
            _originalCwd = AppState.CurrentCwd;
            _originalSessionId = AppState.SessionId;
            _originalActiveProvider = AppState.ActiveProvider;
            _originalActiveModel = AppState.ActiveModel;
            _originalPermissionMode = AppState.CurrentPermissionMode;
            _originalIsProviderExplicitlySet = AppState.IsProviderExplicitlySet;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.SessionId = _originalSessionId;
            AppState.ActiveProvider = _originalActiveProvider;
            AppState.ActiveModel = _originalActiveModel;
            AppState.CurrentPermissionMode = _originalPermissionMode;
            AppState.IsProviderExplicitlySet = _originalIsProviderExplicitlySet;
        }

        [Fact]
        public async Task ToolOrchestrator_ActuallyUsesHookPipeline_Integration()
        {
            var services = new ServiceCollection();
            var pipeline = new HookPipeline();
            var hookBefore = new DummyHookBefore();
            var hookAfter = new DummyHookAfter();
            pipeline.Register(hookBefore);
            pipeline.Register(hookAfter);
            services.AddSingleton(pipeline);

            var serviceProvider = services.BuildServiceProvider();
            var orchestrator = new ToolOrchestrator(new ITool[] { new DummyTool() }, null, serviceProvider);

            var req = new ToolUseRequest { Id = "test-1", Name = "dummy_tool", Input = new { } };
            var result = await orchestrator.ExecuteToolAsync(req, new { });

            Assert.False(result.IsError);
            Assert.Equal("dummy_result", result.Content?.ToString());
            Assert.True(hookBefore.ExecutedBefore);
            Assert.True(hookAfter.ExecutedAfter);
        }

        [Fact]
        public async Task ToolOrchestrator_ActuallyUsesAuditTrailService_Integration()
        {
            var services = new ServiceCollection();
            var auditService = new AuditTrailService(maxEntries: 100);
            services.AddSingleton(auditService);
            services.AddSingleton(new HookPipeline());

            var serviceProvider = services.BuildServiceProvider();
            var orchestrator = new ToolOrchestrator(new ITool[] { new ErrorDummyTool() }, null, serviceProvider);

            var req = new ToolUseRequest { Id = "test-2", Name = "error_dummy_tool", Input = new { } };
            await orchestrator.ExecuteToolAsync(req, new { });

            Assert.True(auditService.Count > 0);
            var log = auditService.GetAll().First(l => l.Action == "error_dummy_tool");
            Assert.Equal(AuditSeverity.Critical, log.Severity);
            Assert.Contains("Error", log.Outcome);
        }

        [Fact]
        public async Task CommandRegistry_Status_ActuallyUsesEventProjectionEngine_Integration()
        {
            string ws = Path.Combine(Path.GetTempPath(), "P1FixTests-Status-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ws);
            AppState.CurrentCwd = ws;
            AppState.SessionId = "status-test-session";

            try
            {
                var services = new ServiceCollection().BuildServiceProvider();
                var cmd = CommandRegistry.FindCommand("status");
                Assert.NotNull(cmd);

                string result = await cmd.Handler!("", services);

                Assert.Contains("Session Projection (CQRS Read Model)", result);
                Assert.Contains("Total Events: 0", result);
            }
            finally
            {
                if (Directory.Exists(ws)) Directory.Delete(ws, true);
            }
        }

        [Fact]
        public void ProviderRegistry_CreateWithDefaults_ContainsStandardProviders()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            Assert.NotNull(registry.Get("claude"));
            Assert.NotNull(registry.Get("gemini"));
            Assert.NotNull(registry.Get("gemini-cli"));
            Assert.NotNull(registry.Get("ollama"));

            var claude = registry.Get("claude");
            Assert.Equal("Anthropic Claude", claude!.Label);
            Assert.Equal("anthropic", claude.TransportKind);
        }

        [Fact]
        public async Task AgentLoop_ResumeCommand_UsesProviderRegistry_Integration()
        {
            // Verify !resume uses ProviderRegistry through the public ListenAsync API.
            // This avoids reflection and exercises the same command path users run.
            var services = new ServiceCollection();

            // Register the real provider registry.
            var registry = ProviderRegistry.CreateWithDefaults();
            services.AddSingleton(registry);

            // Register the minimal services required by AgentLoop.
            services.AddSingleton(new Mock<IEmbeddingProvider>().Object);
            services.AddSingleton(new Mock<ISmartRouter>().Object);

            // Inject provider mocks through delegates because AgentLoop resolves concrete provider types.
            // The mocks keep the test focused on resume wiring rather than provider behavior.

            services.AddSingleton<ClaudeService>(sp => new Mock<ClaudeService>(new Mock<AnthropicClient>(new Mock<System.Net.Http.HttpClient>().Object).Object).Object);
            var toolRegistry = new Mock<IToolRegistry>();
            toolRegistry.Setup(r => r.GetTools()).Returns(new List<ITool>());
            services.AddSingleton(toolRegistry.Object);

            services.AddSingleton<GeminiProvider>(sp => new Mock<GeminiProvider>(new System.Net.Http.HttpClient(), toolRegistry.Object).Object);
            services.AddSingleton<GeminiCliProvider>(sp => new Mock<GeminiCliProvider>().Object);
            services.AddSingleton<OllamaProvider>(sp => new Mock<OllamaProvider>(new System.Net.Http.HttpClient(), toolRegistry.Object).Object);

            var serviceProvider = services.BuildServiceProvider();
            var orchestrator = new ToolOrchestrator(Enumerable.Empty<ITool>(), null, serviceProvider);

            string ws = Path.Combine(Path.GetTempPath(), "P1Fix-Resume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ws);
            AppState.CurrentCwd = ws;
            string sessionId = "test-resume-id";

            try
            {
                // Prepare session data.
                var store = new AgentSessionStore(ws, sessionId);
                await store.InitializeAsync(new AgentSessionRecord {
                    SessionId = sessionId,
                    Provider = "claude",
                    Model = "claude-3-sonnet",
                    StartTime = DateTime.Now
                });

                // Drive the public input path through DummyBroker.
                var broker = new DummyBroker($"!resume {sessionId}");
                var agent = new AgentLoop(orchestrator, serviceProvider, broker, serviceProvider.GetRequiredService<ISmartRouter>());

                using var cts = new CancellationTokenSource();

                try {
                    await agent.ListenAsync(cts.Token);
                }
                catch (OperationCanceledException) { }

                // Verify AppState was updated.
                Assert.Equal(sessionId, AppState.SessionId);
                Assert.Equal("claude", AppState.ActiveProvider);
            }
            finally
            {
                if (Directory.Exists(ws)) Directory.Delete(ws, true);
            }
        }
    }
}
