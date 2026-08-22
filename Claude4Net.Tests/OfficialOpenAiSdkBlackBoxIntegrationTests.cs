using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class OfficialOpenAiSdkBlackBoxIntegrationTests : IAsyncLifetime
    {
        private ServiceProvider _serviceProvider = null!;
        private Claude4NetApiServer _server = null!;
        private int _testPort;
        private const string TestApiKey = "c4n-sk-blackbox-official-sdk-key";

        private string? _origCwd;
        private string _origSessionId = null!;
        private string _origActiveProvider = null!;
        private string _origActiveModel = null!;
        private PermissionMode _origPermissionMode;
        private bool _origIsExplicit;

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task InitializeAsync()
        {
            _origCwd = AppState.CurrentCwd;
            _origSessionId = AppState.SessionId;
            _origActiveProvider = AppState.ActiveProvider;
            _origActiveModel = AppState.ActiveModel;
            _origPermissionMode = AppState.CurrentPermissionMode;
            _origIsExplicit = AppState.IsProviderExplicitlySet;

            var services = new ServiceCollection();
            services.AddSingleton<IProviderFactory, SdkBlackBoxMockProviderFactory>();
            CliServiceRegistration.ConfigureServices(services);
            services.AddSingleton(Wave2TestSupport.CreateOfficialSdkRegistry("mock"));
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
            services.AddSingleton<Claude4NetApiServer>();

            _serviceProvider = services.BuildServiceProvider();
            _server = _serviceProvider.GetRequiredService<Claude4NetApiServer>();

            _testPort = GetAvailablePort();
            await _server.StartAsync(_testPort, TestApiKey);
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }

            AppState.CurrentCwd = _origCwd;
            AppState.SessionId = _origSessionId;
            AppState.ActiveProvider = _origActiveProvider;
            AppState.ActiveModel = _origActiveModel;
            AppState.CurrentPermissionMode = _origPermissionMode;
            AppState.IsProviderExplicitlySet = _origIsExplicit;
        }

        [Fact]
        public async Task FinalControl_OfficialOpenAiPythonSdk_ExecutesCompleteBlackBoxSuite()
        {
            const string successMarker = "ALL 12/12 OFFICIAL OPENAI SDK BLACK-BOX TESTS PASSED!";
            var request = new SdkProcessRequest(
                SdkRuntime.Python,
                "blackbox_openai_sdk_runner.py",
                _testPort,
                TestApiKey,
                successMarker,
                RequiredModule: "openai");

            SdkProcessResult result = await SdkProcessRunner.RunAsync(request);

            Assert.Contains(successMarker, result.StandardOutput);
        }

        private class SdkBlackBoxMockProviderFactory : IProviderFactory
        {
            public string TransportKind => "mock";
            public bool SupportsApiRequests => true;
            public bool CanCreate(ProviderDescriptor descriptor) => true;
            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new SdkBlackBoxMockProvider();
            }

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new SdkBlackBoxMockProvider();
            }
        }

        private class SdkBlackBoxMockProvider : ILLMProvider
        {
            private readonly System.Collections.Generic.List<object> _history = new();
            public string Name => "SdkBlackBoxMockProvider";
            public ITokenCounter TokenCounter { get; } = new SdkBlackBoxTokenCounter();
            public int ContextLimit => 200000;

            public async System.Collections.Generic.IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                if (prompt.Contains("invoke tool", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<invoke name=\"calculator\"><parameter name=\"number\">42</parameter></invoke>" };
                }
                else if (prompt.Contains("reasoning test", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<think>Step 1: Analyzing query</think>The final calculation is ready." };
                }
                else
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Mock blackbox response for: " + prompt };
                }
            }

            public void AddMessage(object message) { if (message != null) _history.Add(message); }
            public System.Collections.Generic.IReadOnlyList<object> GetHistory() => _history;
            public void SetHistory(System.Collections.Generic.IEnumerable<object> history) { _history.Clear(); if (history != null) _history.AddRange(history); }
            public void ClearHistory() => _history.Clear();
        }

        private class SdkBlackBoxTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => Math.Max(1, (text?.Length ?? 0) / 4);
            public int CountTokens(object message) => 10;
            public int CountTokens(System.Collections.Generic.IEnumerable<object> history) => 50;
        }
    }
}
