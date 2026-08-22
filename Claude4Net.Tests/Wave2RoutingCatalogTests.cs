using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests;

[Collection("AppState")]
public sealed class Wave2RoutingCatalogTests
{
    [Theory]
    [InlineData(null, "invalid_model")]
    [InlineData("", "invalid_model")]
    [InlineData("unknown-model", "model_not_found")]
    [InlineData("EXACT-MODEL", "model_not_found")]
    public async Task Chat_MissingUnknownOrCaseMismatchedModel_FailsBeforeFactoryCreation(
        string? model,
        string expectedCode)
    {
        var factory = new Wave2TrackingFactory("wave2-http");
        await using var harness = await RoutingHarness.StartAsync(factory);

        using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            harness.Url("/v1/chat/completions"),
            new ChatCompletionRequest
            {
                Model = model!,
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "hello" } }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.RequestCreateCount);
    }

    [Fact]
    public async Task UnknownModel_FallbacksToActiveProvider_WhenAvailable()
    {
        string originalProvider = AppState.ActiveProvider;
        string originalModel = AppState.ActiveModel;
        var factory = new Wave2TrackingFactory("wave2-http");
        try
        {
            AppState.ActiveProvider = "route-provider";
            AppState.ActiveModel = "exact-model";
            await using var harness = await RoutingHarness.StartAsync(factory);

            using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
                harness.Url("/v1/completions"),
                new TextCompletionRequest { Model = "unknown-model", Prompt = "hello" });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, factory.RequestCreateCount);
        }
        finally
        {
            AppState.ActiveProvider = originalProvider;
            AppState.ActiveModel = originalModel;
        }
    }

    [Fact]
    public async Task Catalog_ContainsOnlyRoutableModels_ExcludesCliOnly_AndEveryAdvertisedModelResolves()
    {
        var httpFactory = new Wave2TrackingFactory("wave2-http");
        var cliFactory = new Wave2TrackingFactory("wave2-cli", supportsApiRequests: false);
        var registry = new ProviderRegistry();
        registry.Register(Wave2TestSupport.Descriptor("route-provider", "wave2-http", "route-small", "route-large"));
        registry.Register(Wave2TestSupport.Descriptor("cli-provider", "wave2-cli", "cli-only-model"));
        registry.RegisterFactory(httpFactory);
        registry.RegisterFactory(cliFactory);
        var embedding = new Wave2EmbeddingProvider("embed-provider", "embed-model");

        await using var harness = await RoutingHarness.StartAsync(registry, embedding);
        ModelListResponse? catalog = await harness.Client.GetFromJsonAsync<ModelListResponse>(harness.Url("/v1/models"));

        Assert.NotNull(catalog);
        Assert.Equal(new[] { "embed-model", "route-large", "route-small" }, catalog.Data.Select(card => card.Id).Order());
        Assert.DoesNotContain(catalog.Data, card => card.Id == "cli-only-model");
        foreach (ModelCardDto card in catalog.Data)
        {
            using HttpResponseMessage detail = await harness.Client.GetAsync(harness.Url($"/v1/models/{card.Id}"));
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

            using HttpResponseMessage routed = card.Id == embedding.ModelId
                ? await harness.Client.PostAsJsonAsync(
                    harness.Url("/v1/embeddings"),
                    new EmbeddingRequest { Model = card.Id, Input = "catalog resolution" })
                : await harness.Client.PostAsJsonAsync(
                    harness.Url("/v1/chat/completions"),
                    new ChatCompletionRequest
                    {
                        Model = card.Id,
                        Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "catalog resolution" } }
                    });
            Assert.Equal(HttpStatusCode.OK, routed.StatusCode);
        }
    }

    [Fact]
    public async Task ExplicitRegistry_AdvertisedModelsResolveThroughThatSameRegistry_WhenDiRegistryDiffers()
    {
        var diFactory = new Wave2TrackingFactory("di-transport");
        var diRegistry = new ProviderRegistry();
        diRegistry.Register(Wave2TestSupport.Descriptor("di-provider", "di-transport", "di-only-model"));
        diRegistry.RegisterFactory(diFactory);

        var explicitFactory = new Wave2TrackingFactory("explicit-transport");
        var explicitRegistry = new ProviderRegistry();
        explicitRegistry.Register(Wave2TestSupport.Descriptor(
            "explicit-provider",
            "explicit-transport",
            "explicit-small-model",
            "explicit-large-model"));
        explicitRegistry.RegisterFactory(explicitFactory);

        await using var harness = await RoutingHarness.StartAsync(diRegistry, explicitRegistry);
        ModelListResponse? catalog = await harness.Client.GetFromJsonAsync<ModelListResponse>(harness.Url("/v1/models"));

        Assert.NotNull(catalog);
        Assert.Equal(
            new[] { "explicit-large-model", "explicit-small-model" },
            catalog.Data.Select(card => card.Id).Order());
        foreach (ModelCardDto card in catalog.Data)
        {
            using HttpResponseMessage response = await PostChatAsync(harness, card.Id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(catalog.Data.Count, explicitFactory.RequestCreateCount);
        Assert.Equal(0, diFactory.RequestCreateCount);
    }

    [Fact]
    public async Task EveryAdvertisedChatModel_ResolvesThroughCatalogRegistry()
    {
        var firstFactory = new Wave2TrackingFactory("first-transport");
        var secondFactory = new Wave2TrackingFactory("second-transport");
        var registry = new ProviderRegistry();
        registry.Register(Wave2TestSupport.Descriptor("first-provider", "first-transport", "first-model"));
        registry.Register(Wave2TestSupport.Descriptor("second-provider", "second-transport", "second-model"));
        registry.RegisterFactory(firstFactory);
        registry.RegisterFactory(secondFactory);

        await using var harness = await RoutingHarness.StartAsync(registry);
        ModelListResponse? catalog = await harness.Client.GetFromJsonAsync<ModelListResponse>(harness.Url("/v1/models"));

        Assert.NotNull(catalog);
        foreach (ModelCardDto card in catalog.Data)
        {
            using HttpResponseMessage response = await PostChatAsync(harness, card.Id);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("provider_error", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Catalog_DuplicateModelAcrossProviders_AllowsStartupGracefully()
    {
        var registry = new ProviderRegistry();
        registry.Register(Wave2TestSupport.Descriptor("provider-a", "wave2-http", "duplicate-model"));
        registry.Register(Wave2TestSupport.Descriptor("provider-b", "wave2-http", "duplicate-model"));
        registry.RegisterFactory(new Wave2TrackingFactory("wave2-http"));
        await using var services = BuildServices(registry);
        Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

        try
        {
            await server.StartAsync(Wave2TestSupport.GetAvailablePort(), Wave2TestSupport.ApiKey);
            Assert.True(server.IsRunning);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static ServiceProvider BuildServices(ProviderRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton<Claude4NetApiServer>();
        return services.BuildServiceProvider();
    }

    private static Task<HttpResponseMessage> PostChatAsync(RoutingHarness harness, string model)
        => harness.Client.PostAsJsonAsync(
            harness.Url("/v1/chat/completions"),
            new ChatCompletionRequest
            {
                Model = model,
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "catalog resolution" } }
            });

    private sealed class RoutingHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private RoutingHarness(ServiceProvider services, Claude4NetApiServer server, HttpClient client, int port)
        {
            _services = services;
            Server = server;
            Client = client;
            Port = port;
        }

        public Claude4NetApiServer Server { get; }
        public HttpClient Client { get; }
        public int Port { get; }
        public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

        public static Task<RoutingHarness> StartAsync(Wave2TrackingFactory factory)
        {
            var registry = new ProviderRegistry();
            registry.Register(Wave2TestSupport.Descriptor("route-provider", "wave2-http", "exact-model"));
            registry.RegisterFactory(factory);
            return StartAsync(registry);
        }

        public static async Task<RoutingHarness> StartAsync(
            ProviderRegistry registry,
            params IEmbeddingProvider[] embeddingProviders)
        {
            var services = new ServiceCollection();
            services.AddSingleton(registry);
            foreach (IEmbeddingProvider provider in embeddingProviders)
            {
                services.AddSingleton<IEmbeddingProvider>(provider);
            }
            services.AddSingleton<Claude4NetApiServer>();
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            Claude4NetApiServer server = serviceProvider.GetRequiredService<Claude4NetApiServer>();
            int port = Wave2TestSupport.GetAvailablePort();
            await server.StartAsync(port, Wave2TestSupport.ApiKey);
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                Wave2TestSupport.ApiKey);
            return new RoutingHarness(serviceProvider, server, client, port);
        }

        public static async Task<RoutingHarness> StartAsync(
            ProviderRegistry registeredRegistry,
            ProviderRegistry explicitRegistry)
        {
            var services = new ServiceCollection();
            services.AddSingleton(registeredRegistry);
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            var server = new Claude4NetApiServer(serviceProvider, explicitRegistry);
            int port = Wave2TestSupport.GetAvailablePort();
            await server.StartAsync(port, Wave2TestSupport.ApiKey);
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                Wave2TestSupport.ApiKey);
            return new RoutingHarness(serviceProvider, server, client, port);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Server.StopAsync();
            await _services.DisposeAsync();
        }
    }
}
