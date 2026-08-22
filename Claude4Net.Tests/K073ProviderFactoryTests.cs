using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Cli.Bootstrap;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Claude4Net.Tests
{
    public class K073ProviderFactoryTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IToolRegistry> _mockToolRegistry;

        public K073ProviderFactoryTests()
        {
            var services = new ServiceCollection();

            _mockToolRegistry = new Mock<IToolRegistry>();
            services.AddSingleton(_mockToolRegistry.Object);

            // Register real clients/dependencies
            services.AddSingleton<AnthropicClient>(sp => new AnthropicClient(new HttpClient()));

            // Register real provider instances
            services.AddSingleton<ClaudeService>();
            services.AddSingleton<GeminiProvider>(sp => new GeminiProvider(new HttpClient(), _mockToolRegistry.Object));
            services.AddSingleton<OllamaProvider>(sp => new OllamaProvider(new HttpClient(), _mockToolRegistry.Object));
            services.AddSingleton<GeminiCliProvider>();
            services.AddHttpClient();

            // Factories
            services.AddSingleton<IProviderFactory, AnthropicProviderFactory>();
            services.AddSingleton<IProviderFactory, GeminiProviderFactory>();
            services.AddSingleton<IProviderFactory, OllamaProviderFactory>();
            services.AddSingleton<IProviderFactory, GeminiCliProviderFactory>();
            services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();

            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void HappyPath_AnthropicProviderFactory_ShouldCreateClaudeService()
        {
            var factory = new AnthropicProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "claude",
                Label = "Claude Provider",
                TransportKind = "anthropic",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<ClaudeService>(provider);
        }

        [Fact]
        public void HappyPath_GeminiProviderFactory_ShouldCreateGeminiProvider()
        {
            var factory = new GeminiProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "gemini",
                Label = "Gemini Provider",
                TransportKind = "gemini-native",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<GeminiProvider>(provider);
        }

        [Fact]
        public void HappyPath_OllamaProviderFactory_ShouldCreateOllamaProvider()
        {
            var factory = new OllamaProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "ollama",
                Label = "Ollama Provider",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OllamaProvider>(provider);
        }

        [Fact]
        public void HappyPath_GeminiCliProviderFactory_ShouldCreateGeminiCliProvider()
        {
            var factory = new GeminiCliProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "gemini-cli",
                Label = "Gemini CLI Provider",
                TransportKind = "cli",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<GeminiCliProvider>(provider);
        }

        [Fact]
        public void HappyPath_OpenAiCompatProviderFactory_ShouldCreateOpenAiCompatProvider()
        {
            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai",
                Label = "Custom OpenAI Compatible Provider",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth
                {
                    Mode = "none"
                }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OpenAiCompatProvider>(provider);
            Assert.Equal("custom-openai", provider.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid-url")]
        [InlineData("ftp://api.com")]
        public void OpenAiCompatProviderFactory_ShouldThrow_OnMalformedOrEmptyEndpoint(string endpoint)
        {
            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai",
                Label = "Custom OpenAI",
                TransportKind = "openai-compat",
                Endpoint = endpoint,
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth { Mode = "none" }
            };

            Assert.Throws<ArgumentException>(() => factory.Create(descriptor, _serviceProvider));
        }

        [Fact]
        public void OpenAiCompatProviderFactory_ShouldThrow_WhenApiKeyMissing()
        {
            string envVarName = "TEST_K073_MISSING_API_KEY";
            Environment.SetEnvironmentVariable(envVarName, null);

            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai-missing",
                Label = "Custom OpenAI Missing Key",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { envVarName }
                }
            };

            Assert.Throws<InvalidOperationException>(() => factory.Create(descriptor, _serviceProvider));
        }

        [Fact]
        public void OpenAiCompatProviderFactory_ShouldSucceed_WhenApiKeyExists()
        {
            string envVarName = "TEST_K073_EXISTS_API_KEY";
            Environment.SetEnvironmentVariable(envVarName, "test-key-value");

            try
            {
                var factory = new OpenAiCompatProviderFactory();
                var descriptor = new ProviderDescriptor
                {
                    Id = "custom-openai-exists",
                    Label = "Custom OpenAI Exists Key",
                    TransportKind = "openai-compat",
                    Endpoint = "https://api.openai.com/v1",
                    DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                    Auth = new ProviderAuth
                    {
                        Mode = "api-key",
                        EnvVars = new[] { envVarName }
                    }
                };

                var provider = factory.Create(descriptor, _serviceProvider);
                Assert.NotNull(provider);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        [Fact]
        public void ProviderRegistry_CreateProvider_ShouldUseFactoryFirst()
        {
            var registry = new ProviderRegistry();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-factory-prov",
                Label = "Custom Factory Provider",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth { Mode = "none" }
            };
            registry.Register(descriptor);

            var provider = registry.CreateProvider("custom-factory-prov", _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OpenAiCompatProvider>(provider);
        }

        [Fact]
        public void ProviderRegistry_CreateProvider_ShouldFallbackSafely_WhenNoDescriptorOrFactory()
        {
            var registry = new ProviderRegistry();
            var provider = registry.CreateProvider("non-existent", _serviceProvider);

            Assert.NotNull(provider);
            Assert.IsType<ClaudeService>(provider);
        }

        [Fact]
        public void ApiSupport_IsExplicitForEveryBuiltInFactory()
        {
            Assert.True(new AnthropicProviderFactory().SupportsApiRequests);
            Assert.True(new GeminiProviderFactory().SupportsApiRequests);
            Assert.True(new OllamaProviderFactory().SupportsApiRequests);
            Assert.True(new GlmProviderFactory().SupportsApiRequests);
            Assert.True(new OpenAiCompatProviderFactory().SupportsApiRequests);
            Assert.True(new GeminiCliProviderFactory().SupportsApiRequests);
            Assert.True(new AntigravityCliProviderFactory().SupportsApiRequests);
        }

        [Fact]
        public void CliServiceRegistration_PreservesGeminiAsRootEmbeddingProvider()
        {
            var services = new ServiceCollection();
            CliServiceRegistration.ConfigureServices(services);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            IEmbeddingProvider rootProvider = serviceProvider.GetRequiredService<IEmbeddingProvider>();
            string[] registeredModels = serviceProvider.GetServices<IEmbeddingProvider>()
                .Select(provider => provider.ModelId)
                .Order()
                .ToArray();

            Assert.IsType<GeminiEmbeddingProvider>(rootProvider);
            Assert.Equal(new[] { "embedding-3", "text-embedding-004" }, registeredModels);
        }

        [Fact]
        public void CliFactories_CreateSandboxedRequestProviders()
        {
            ProviderDescriptor geminiCli = CreateDescriptor("gemini-cli", "cli");
            ProviderDescriptor antigravityCli = CreateDescriptor("antigravity-cli", "cli");

            ILLMProvider geminiProvider = new GeminiCliProviderFactory().CreateRequestProvider(geminiCli, _serviceProvider);
            ILLMProvider antigravityProvider = new AntigravityCliProviderFactory().CreateRequestProvider(antigravityCli, _serviceProvider);

            Assert.NotNull(geminiProvider);
            Assert.NotNull(antigravityProvider);
        }

        [Theory]
        [InlineData("claude", "anthropic")]
        [InlineData("gemini", "gemini-native")]
        [InlineData("ollama", "openai-compat")]
        [InlineData("glm", "glm")]
        [InlineData("custom-openai", "openai-compat")]
        public void ApiCapableFactories_CreateFreshRequestProviders(string providerId, string transportKind)
        {
            IProviderFactory factory = providerId switch
            {
                "claude" => new AnthropicProviderFactory(),
                "gemini" => new GeminiProviderFactory(),
                "ollama" => new OllamaProviderFactory(),
                "glm" => new GlmProviderFactory(),
                _ => new OpenAiCompatProviderFactory()
            };
            ProviderDescriptor descriptor = CreateDescriptor(providerId, transportKind);

            ILLMProvider first = factory.CreateRequestProvider(descriptor, _serviceProvider);
            ILLMProvider second = factory.CreateRequestProvider(descriptor, _serviceProvider);

            Assert.NotSame(first, second);
        }

        [Theory]
        [InlineData("claude", "anthropic")]
        [InlineData("gemini", "gemini-native")]
        [InlineData("ollama", "openai-compat")]
        [InlineData("glm", "glm")]
        [InlineData("custom-openai", "openai-compat")]
        public void ApiCapableFactories_RequestProvidersUseSealedEmptyRegistry(string providerId, string transportKind)
        {
            IProviderFactory factory = CreateApiFactory(providerId);
            ILLMProvider provider = factory.CreateRequestProvider(CreateDescriptor(providerId, transportKind), _serviceProvider);
            FieldInfo registryField = provider.GetType().GetField("_toolRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var requestRegistry = Assert.IsAssignableFrom<IToolRegistry>(registryField.GetValue(provider));

            Assert.NotSame(_mockToolRegistry.Object, requestRegistry);
            Assert.Empty(requestRegistry.GetTools());
            Assert.Null(requestRegistry.GetTool("anything"));
            Assert.True(requestRegistry.GetType().IsSealed);

            ILLMProvider secondProvider = factory.CreateRequestProvider(CreateDescriptor(providerId, transportKind), _serviceProvider);
            var secondRegistry = Assert.IsAssignableFrom<IToolRegistry>(registryField.GetValue(secondProvider));
            Assert.Same(requestRegistry, secondRegistry);
        }

        [Theory]
        [InlineData("claude", "anthropic")]
        [InlineData("gemini", "gemini-native")]
        [InlineData("ollama", "openai-compat")]
        [InlineData("glm", "glm")]
        [InlineData("custom-openai", "openai-compat")]
        public void ApiCapableFactories_MissingHttpClientFactory_FailsDeterministically(string providerId, string transportKind)
        {
            var services = new ServiceCollection();
            services.AddSingleton(_mockToolRegistry.Object);
            services.AddSingleton(new AnthropicClient(new HttpClient()));
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IProviderFactory factory = CreateApiFactory(providerId);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                factory.CreateRequestProvider(CreateDescriptor(providerId, transportKind), serviceProvider));

            Assert.Contains(nameof(IHttpClientFactory), error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task BuiltInRequestProviderLeases_OwnTheirCreatedHttpClients()
        {
            var httpClientFactory = new TrackingHttpClientFactory();
            var services = new ServiceCollection();
            services.AddSingleton<IHttpClientFactory>(httpClientFactory);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            foreach ((string providerId, string transportKind) in new[]
            {
                ("claude", "anthropic"),
                ("gemini", "gemini-native"),
                ("ollama", "openai-compat"),
                ("glm", "glm"),
                ("custom-openai", "openai-compat")
            })
            {
                RequestProviderLease lease = CreateApiFactory(providerId)
                    .CreateRequestProviderLease(CreateDescriptor(providerId, transportKind), serviceProvider);
                await lease.DisposeAsync();
                await lease.DisposeAsync();
            }

            Assert.Equal(5, httpClientFactory.Clients.Count);
            Assert.All(httpClientFactory.Clients, client => Assert.Equal(1, client.DisposeCount));
            Assert.Equal(
                new[] { "Anthropic", "Gemini", "Ollama", "glm", "OpenAiCompat" },
                httpClientFactory.ClientNames);
        }

        [Fact]
        public async Task DefaultPluginLease_DoesNotDisposeExternallyOwnedProvider()
        {
            var provider = new DisposableTrackingProvider();
            IProviderFactory factory = new DefaultLeaseFactory(provider);

            RequestProviderLease lease = factory.CreateRequestProviderLease(
                CreateDescriptor("plugin", "plugin"),
                _serviceProvider);
            await lease.DisposeAsync();

            Assert.Same(provider, lease.Provider);
            Assert.Equal(0, provider.DisposeCount);
        }

        [Fact]
        public void OpenAiCompatProviderFactory_RejectsRemoteHttpEndpoint()
        {
            var handler = new RecordingOpenAiHandler();
            using ServiceProvider serviceProvider = CreateOpenAiServiceProvider(handler);
            var factory = new OpenAiCompatProviderFactory();
            ProviderDescriptor descriptor = CreateDescriptor("remote-http", "openai-compat") with
            {
                Endpoint = "http://192.0.2.1/v1"
            };

            Exception? createError = Record.Exception(() => factory.Create(descriptor, serviceProvider));
            Exception? requestProviderError = Record.Exception(() => factory.CreateRequestProvider(descriptor, serviceProvider));

            Assert.IsType<ArgumentException>(createError);
            Assert.IsType<ArgumentException>(requestProviderError);
            Assert.Equal(0, handler.SendCount);
        }

        [Fact]
        public async Task OpenAiCompatProvider_EndpointFromKeySyntax_IsNeverInterpretedAsEndpoint()
        {
            const string environmentVariable = "TEST_K073_ENDPOINT_SHAPED_KEY";
            const string credential = "http://192.0.2.1/v1 secret";
            Environment.SetEnvironmentVariable(environmentVariable, credential);

            try
            {
                var handler = new RecordingOpenAiHandler();
                using var httpClient = new HttpClient(handler);
                ProviderDescriptor descriptor = CreateDescriptor("endpoint-shaped-key", "openai-compat") with
                {
                    Endpoint = "https://api.example.test/v1",
                    Auth = new ProviderAuth { Mode = "api-key", EnvVars = new[] { environmentVariable } }
                };
                var provider = new OpenAiCompatProvider(httpClient, _mockToolRegistry.Object, descriptor);

                await ConsumeChatAsync(provider);
                await provider.GetEmbeddingsAsync(new[] { "embedding input" });

                Assert.Equal(
                    new[]
                    {
                        "https://api.example.test/v1/chat/completions",
                        "https://api.example.test/v1/embeddings"
                    },
                    handler.RequestUris.Select(uri => uri.AbsoluteUri));
                Assert.DoesNotContain(handler.RequestUris, uri => uri.Host == "192.0.2.1");
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, null);
            }
        }

        [Fact]
        public async Task OpenAiCompatProvider_FinalSendRevalidatesEndpoint()
        {
            var handler = new RecordingOpenAiHandler();
            using var httpClient = new HttpClient(handler);
            ProviderDescriptor descriptor = CreateDescriptor("direct-remote-http", "openai-compat") with
            {
                Endpoint = "http://192.0.2.1/v1"
            };
            var provider = new OpenAiCompatProvider(httpClient, _mockToolRegistry.Object, descriptor);

            await Assert.ThrowsAsync<ArgumentException>(() => ConsumeChatAsync(provider));
            Assert.Equal(0, handler.SendCount);
        }

        [Fact]
        public async Task LmStudioDiscovery_RejectsRemoteHttpAndAllowsSecureOrLoopbackEndpoints()
        {
            await AssertLmStudioEndpointPolicyAsync(Claude4Net.Commands.Handlers.ProviderCommands.HandleModel);
            await AssertLmStudioEndpointPolicyAsync(Claude4Net.Runtime.Handlers.ProviderCommands.HandleModel);
        }

        [Theory]
        [InlineData("Anthropic")]
        [InlineData("Gemini")]
        [InlineData("glm")]
        [InlineData("Ollama")]
        [InlineData("lmstudio")]
        [InlineData("OpenAiCompat")]
        public void CliServiceRegistration_ProviderHttpClientsDisableAutomaticRedirects(string clientName)
        {
            var services = new ServiceCollection();
            CliServiceRegistration.ConfigureServices(services);
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            HttpClientFactoryOptions options = serviceProvider
                .GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>()
                .Get(clientName);
            var builder = new TestHttpMessageHandlerBuilder(serviceProvider)
            {
                Name = clientName,
                PrimaryHandler = new HttpClientHandler()
            };

            foreach (Action<HttpMessageHandlerBuilder> action in options.HttpMessageHandlerBuilderActions)
            {
                action(builder);
            }

            var handler = Assert.IsType<HttpClientHandler>(builder.PrimaryHandler);
            Assert.False(handler.AllowAutoRedirect);
        }

        private static async Task AssertLmStudioEndpointPolicyAsync(
            Func<string, IServiceProvider, Task<string>> handleModel)
        {
            const string environmentVariable = "LMSTUDIO_API_KEY";
            string? originalEndpoint = Environment.GetEnvironmentVariable(environmentVariable);
            string originalProvider = AppState.ActiveProvider;
            string originalModel = AppState.ActiveModel;
            try
            {
                Environment.SetEnvironmentVariable(environmentVariable, "http://192.0.2.1:1234 test-token");
                var remoteFactory = new LmStudioHttpClientFactory();
                using (ServiceProvider serviceProvider = CreateLmStudioServiceProvider(remoteFactory))
                {
                    AppState.ActiveProvider = "test-provider";
                    await handleModel("lm-test", serviceProvider);
                    Assert.Equal(0, remoteFactory.SendCount);
                }

                foreach (string endpoint in new[]
                {
                    "http://127.0.0.1:11434",
                    "http://localhost:1234",
                    "https://lmstudio.example.test"
                })
                {
                    Environment.SetEnvironmentVariable(environmentVariable, $"{endpoint} test-token");
                    var allowedFactory = new LmStudioHttpClientFactory();
                    using ServiceProvider serviceProvider = CreateLmStudioServiceProvider(allowedFactory);
                    AppState.ActiveProvider = "test-provider";

                    await handleModel("lm-test", serviceProvider);

                    Assert.Equal(1, allowedFactory.SendCount);
                    Assert.Equal("lmstudio", AppState.ActiveProvider);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalEndpoint);
                AppState.ActiveProvider = originalProvider;
                AppState.ActiveModel = originalModel;
            }
        }

        private static ServiceProvider CreateLmStudioServiceProvider(LmStudioHttpClientFactory factory)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHttpClientFactory>(factory);
            services.AddSingleton(new OllamaProvider(
                new HttpClient(new OllamaModelsHandler()),
                Mock.Of<IToolRegistry>()));
            return services.BuildServiceProvider();
        }

        private static async Task ConsumeChatAsync(OpenAiCompatProvider provider)
        {
            await foreach (LLMStreamEvent _ in provider.StreamQueryAsync("test prompt"))
            {
            }
        }

        private static ServiceProvider CreateOpenAiServiceProvider(RecordingOpenAiHandler handler)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IToolRegistry>(Mock.Of<IToolRegistry>());
            services.AddSingleton<IHttpClientFactory>(new RecordingHttpClientFactory(handler));
            return services.BuildServiceProvider();
        }

        private static IProviderFactory CreateApiFactory(string providerId) => providerId switch
        {
            "claude" => new AnthropicProviderFactory(),
            "gemini" => new GeminiProviderFactory(),
            "ollama" => new OllamaProviderFactory(),
            "glm" => new GlmProviderFactory(),
            _ => new OpenAiCompatProviderFactory()
        };

        private static ProviderDescriptor CreateDescriptor(string providerId, string transportKind)
        {
            return new ProviderDescriptor
            {
                Id = providerId,
                Label = providerId,
                TransportKind = transportKind,
                Endpoint = transportKind == "openai-compat" ? "https://example.test/v1" : string.Empty,
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth { Mode = "none" }
            };
        }

        private sealed class RecordingHttpClientFactory : IHttpClientFactory
        {
            private readonly RecordingOpenAiHandler _handler;

            public RecordingHttpClientFactory(RecordingOpenAiHandler handler)
            {
                _handler = handler;
            }

            public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
        }

        private sealed class TrackingHttpClientFactory : IHttpClientFactory
        {
            public List<TrackingHttpClient> Clients { get; } = new();
            public List<string> ClientNames { get; } = new();

            public HttpClient CreateClient(string name)
            {
                var client = new TrackingHttpClient();
                Clients.Add(client);
                ClientNames.Add(name);
                return client;
            }
        }

        private sealed class TrackingHttpClient : HttpClient
        {
            public int DisposeCount { get; private set; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCount++;
                }
                base.Dispose(disposing);
            }
        }

        private sealed class DefaultLeaseFactory : IProviderFactory
        {
            private readonly ILLMProvider _provider;

            public DefaultLeaseFactory(ILLMProvider provider)
            {
                _provider = provider;
            }

            public bool SupportsApiRequests => true;
            public bool CanCreate(ProviderDescriptor descriptor) => true;
            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider) => _provider;
            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider) => _provider;
        }

        private sealed class DisposableTrackingProvider : ILLMProvider, IDisposable
        {
            public string Name => "Disposable tracking provider";
            public int ContextLimit => 1024;
            public ITokenCounter TokenCounter { get; } = Mock.Of<ITokenCounter>();
            public int DisposeCount { get; private set; }

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                yield break;
            }

            public void AddMessage(object message) { }
            public IReadOnlyList<object> GetHistory() => Array.Empty<object>();
            public void SetHistory(IEnumerable<object> history) { }
            public void Dispose() => DisposeCount++;
        }

        private sealed class RecordingOpenAiHandler : HttpMessageHandler
        {
            private readonly ConcurrentQueue<Uri> _requestUris = new();

            public int SendCount => _requestUris.Count;

            public IReadOnlyList<Uri> RequestUris => _requestUris.ToArray();

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                _requestUris.Enqueue(request.RequestUri!);
                string content = request.RequestUri!.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal)
                    ? "{\"data\":[{\"embedding\":[0.25,0.75]}]}"
                    : "data: [DONE]\n\n";

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
        }

        private sealed class LmStudioHttpClientFactory : IHttpClientFactory
        {
            private readonly LmStudioHandler _handler = new();

            public int SendCount => _handler.SendCount;

            public HttpClient CreateClient(string name)
            {
                Assert.Equal("lmstudio", name);
                return new HttpClient(_handler, disposeHandler: false);
            }
        }

        private sealed class LmStudioHandler : HttpMessageHandler
        {
            public int SendCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                SendCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":[{\"id\":\"lm-test\"}]}")
                });
            }
        }

        private sealed class OllamaModelsHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"models\":[]}")
                });
        }

        private sealed class TestHttpMessageHandlerBuilder : HttpMessageHandlerBuilder
        {
            public TestHttpMessageHandlerBuilder(IServiceProvider services)
            {
                Services = services;
            }

            public override string? Name { get; set; }
            public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();
            public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
            public override IServiceProvider Services { get; }

            public override HttpMessageHandler Build() =>
                CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
        }
    }
}
