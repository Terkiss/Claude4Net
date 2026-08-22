using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public sealed class ApiProviderIsolationTests : IAsyncLifetime
    {
        private const string ApiKey = "c4n-isolation-test-key";
        private const string ProviderId = "request-isolation";
        private readonly HttpClient _client = new();
        private readonly RequestIsolationFactory _factory = new();
        private string _originalActiveProvider = null!;
        private string _originalActiveModel = null!;
        private bool _originalIsProviderExplicitlySet;
        private ServiceProvider _services = null!;
        private Claude4NetApiServer _server = null!;
        private int _port;

        public async Task InitializeAsync()
        {
            _originalActiveProvider = AppState.ActiveProvider;
            _originalActiveModel = AppState.ActiveModel;
            _originalIsProviderExplicitlySet = AppState.IsProviderExplicitlySet;

            AppState.ActiveProvider = ProviderId;
            AppState.ActiveModel = "request-isolation-model";
            AppState.IsProviderExplicitlySet = true;

            var registry = new ProviderRegistry();
            registry.Register(new ProviderDescriptor
            {
                Id = ProviderId,
                Label = "Request isolation test provider",
                TransportKind = "request-isolation",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "request-isolation-model",
                    Large = "request-isolation-model"
                }
            });
            registry.RegisterFactory(_factory);

            var services = new ServiceCollection();
            services.AddSingleton(registry);
            services.AddSingleton<Claude4NetApiServer>();
            _services = services.BuildServiceProvider();
            _server = _services.GetRequiredService<Claude4NetApiServer>();
            _port = GetAvailablePort();
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
            await _server.StartAsync(_port, ApiKey);
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (_server != null)
                {
                    await _server.StopAsync();
                }
            }
            finally
            {
                _client.Dispose();
                if (_services != null)
                {
                    await _services.DisposeAsync();
                }

                AppState.ActiveProvider = _originalActiveProvider;
                AppState.ActiveModel = _originalActiveModel;
                AppState.IsProviderExplicitlySet = _originalIsProviderExplicitlySet;
            }
        }

        [Fact]
        public async Task ChatCompletions_UsesFreshRequestProvider_NotSharedCliProvider()
        {
            const string firstPrompt = "isolation-first-prompt";
            const string secondPrompt = "isolation-second-prompt";

            var responses = await Task.WhenAll(
                SendChatCompletionAsync(firstPrompt),
                SendChatCompletionAsync(secondPrompt));

            using var firstResponse = responses[0];
            using var secondResponse = responses[1];
            Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

            string firstBody = await firstResponse.Content.ReadAsStringAsync();
            string secondBody = await secondResponse.Content.ReadAsStringAsync();
            Assert.Contains(firstPrompt, firstBody);
            Assert.DoesNotContain(secondPrompt, firstBody);
            Assert.Contains(secondPrompt, secondBody);
            Assert.DoesNotContain(firstPrompt, secondBody);

            Assert.Equal(0, _factory.CliCreateCount);
            Assert.Equal(2, _factory.RequestCreateCount);
            Assert.Equal(2, _factory.RequestProviders.Count);
            Assert.Equal(2, _factory.RequestProviders.Distinct().Count());
            Assert.DoesNotContain(StatefulProvider.ContaminationMarker, firstBody);
            Assert.DoesNotContain(StatefulProvider.ContaminationMarker, secondBody);
            Assert.Equal(new object[] { StatefulProvider.SeededHistory }, _factory.SharedProvider.GetHistory());
        }

        [Theory]
        [InlineData("/v1/chat/completions")]
        [InlineData("/v1/completions")]
        public async Task RequestProviderLease_IsDisposed_AfterChatAndLegacyCompletionSuccess(string endpoint)
        {
            using HttpResponseMessage response = await SendCompletionAsync(endpoint, "lease-success");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await response.Content.ReadAsStringAsync();
            Assert.Equal(1, _factory.LeaseAcquisitionCount);
            Assert.Equal(1, _factory.LeaseDisposalCount);
        }

        [Fact]
        public async Task RequestProviderLease_IsDisposed_AfterProviderFailure()
        {
            _factory.Behavior = RequestProviderBehavior.Throw;

            using HttpResponseMessage response = await SendChatCompletionAsync("lease-failure");
            string body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            Assert.Contains("provider_error", body, StringComparison.Ordinal);
            Assert.Equal(1, _factory.LeaseAcquisitionCount);
            Assert.Equal(1, _factory.LeaseDisposalCount);
        }

        [Fact]
        public async Task RequestProviderLease_IsDisposed_AfterRequestCancellation()
        {
            _factory.Behavior = RequestProviderBehavior.Block;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<HttpResponseMessage> request = SendChatCompletionAsync("lease-cancellation", cancellation.Token);
            await _factory.ProviderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
            await _factory.LeaseDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, _factory.LeaseAcquisitionCount);
            Assert.Equal(1, _factory.LeaseDisposalCount);
        }

        private Task<HttpResponseMessage> SendChatCompletionAsync(string prompt, CancellationToken ct = default)
        {
            return _client.PostAsJsonAsync($"http://localhost:{_port}/v1/chat/completions", new ChatCompletionRequest
            {
                Model = AppState.ActiveModel,
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = prompt }
                },
                Stream = false
            }, ct);
        }

        private Task<HttpResponseMessage> SendCompletionAsync(string endpoint, string prompt)
        {
            if (endpoint == "/v1/chat/completions")
            {
                return SendChatCompletionAsync(prompt);
            }

            return _client.PostAsJsonAsync($"http://localhost:{_port}{endpoint}", new
            {
                model = AppState.ActiveModel,
                prompt,
                stream = false
            });
        }

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public sealed class RequestIsolationFactory : IProviderFactory
        {
            private int _cliCreateCount;
            private int _leaseAcquisitionCount;
            private int _leaseDisposalCount;
            private int _requestCreateCount;

            public StatefulProvider SharedProvider { get; } = new();
            public ConcurrentBag<RequestEchoProvider> RequestProviders { get; } = new();
            public RequestProviderBehavior Behavior { get; set; }
            public int CliCreateCount => Volatile.Read(ref _cliCreateCount);
            public int LeaseAcquisitionCount => Volatile.Read(ref _leaseAcquisitionCount);
            public int LeaseDisposalCount => Volatile.Read(ref _leaseDisposalCount);
            public int RequestCreateCount => Volatile.Read(ref _requestCreateCount);
            public bool SupportsApiRequests => true;
            public TaskCompletionSource LeaseDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource ProviderStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool CanCreate(ProviderDescriptor descriptor) => descriptor.Id == ProviderId;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                Interlocked.Increment(ref _cliCreateCount);
                return SharedProvider;
            }

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                Interlocked.Increment(ref _requestCreateCount);
                return Behavior switch
                {
                    RequestProviderBehavior.Throw => new ThrowingProvider(),
                    RequestProviderBehavior.Block => new BlockingProvider(ProviderStarted),
                    _ => CreateEchoProvider()
                };
            }

            public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                Interlocked.Increment(ref _leaseAcquisitionCount);
                return new RequestProviderLease(
                    CreateRequestProvider(descriptor, serviceProvider),
                    new DisposalTracker(this));
            }

            private RequestEchoProvider CreateEchoProvider()
            {
                var provider = new RequestEchoProvider();
                RequestProviders.Add(provider);
                return provider;
            }

            private sealed class DisposalTracker : IDisposable
            {
                private readonly RequestIsolationFactory _factory;

                public DisposalTracker(RequestIsolationFactory factory)
                {
                    _factory = factory;
                }

                public void Dispose()
                {
                    Interlocked.Increment(ref _factory._leaseDisposalCount);
                    _factory.LeaseDisposed.TrySetResult();
                }
            }
        }

        public enum RequestProviderBehavior
        {
            Echo,
            Throw,
            Block
        }

        public sealed class StatefulProvider : ILLMProvider
        {
            public const string SeededHistory = "CLI_SHARED_SEEDED_HISTORY";
            public const string ContaminationMarker = "CLI_SHARED_CONTAMINATED_RESPONSE";

            private readonly List<object> _history = new() { SeededHistory };

            public string Name => "Shared CLI provider";
            public int ContextLimit => 1024;
            public ITokenCounter TokenCounter { get; } = new IsolationTokenCounter();

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = $"{ContaminationMarker}:{SeededHistory}:{prompt}"
                };
            }

            public void AddMessage(object message) => _history.Add(message);

            public IReadOnlyList<object> GetHistory() => _history.ToArray();

            public void SetHistory(IEnumerable<object> history)
            {
                _history.Clear();
                _history.AddRange(history);
            }
        }

        public sealed class RequestEchoProvider : ILLMProvider
        {
            private readonly List<object> _history = new();

            public string Name => "Request-local echo provider";
            public int ContextLimit => 1024;
            public ITokenCounter TokenCounter { get; } = new IsolationTokenCounter();

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = $"REQUEST_LOCAL_ECHO:{prompt}"
                };
            }

            public void AddMessage(object message) => _history.Add(message);

            public IReadOnlyList<object> GetHistory() => _history.ToArray();

            public void SetHistory(IEnumerable<object> history)
            {
                _history.Clear();
                _history.AddRange(history);
            }
        }

        private sealed class ThrowingProvider : ILLMProvider
        {
            public string Name => "Throwing request provider";
            public int ContextLimit => 1024;
            public ITokenCounter TokenCounter { get; } = new IsolationTokenCounter();

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield return await Task.FromException<LLMStreamEvent>(new InvalidOperationException("Provider failed."));
            }

            public void AddMessage(object message) { }
            public IReadOnlyList<object> GetHistory() => Array.Empty<object>();
            public void SetHistory(IEnumerable<object> history) { }
        }

        private sealed class BlockingProvider : ILLMProvider
        {
            private readonly TaskCompletionSource _started;

            public BlockingProvider(TaskCompletionSource started)
            {
                _started = started;
            }

            public string Name => "Blocking request provider";
            public int ContextLimit => 1024;
            public ITokenCounter TokenCounter { get; } = new IsolationTokenCounter();

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                _started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                yield break;
            }

            public void AddMessage(object message) { }
            public IReadOnlyList<object> GetHistory() => Array.Empty<object>();
            public void SetHistory(IEnumerable<object> history) { }
        }

        private sealed class IsolationTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => Math.Max(1, text.Length);

            public int CountTokens(object message) => 1;

            public int CountTokens(IEnumerable<object> messages) => messages.Count();
        }
    }
}
