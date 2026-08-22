using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests;

public sealed class Wave2EmbeddingAndFactoryTests
{
    [Fact]
    public async Task Embeddings_UnknownModel_ReturnsModelNotFoundWithoutProviderCall()
    {
        var provider = new Wave2EmbeddingProvider("embed-provider", "registered-embedding");
        await using var harness = await EmbeddingHarness.StartAsync(provider);

        using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            harness.Url("/v1/embeddings"),
            new EmbeddingRequest { Model = "unknown-embedding", Input = "must not be embedded" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("model_not_found", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Embeddings_ResponseUsesRegisteredProviderModelIdentity()
    {
        var provider = new Wave2EmbeddingProvider("embed-provider", "registered-embedding");
        await using var harness = await EmbeddingHarness.StartAsync(provider);

        using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            harness.Url("/v1/embeddings"),
            new EmbeddingRequest { Model = provider.ModelId, Input = "embed me" });
        EmbeddingResponse? body = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(provider.ModelId, body.Model);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Embeddings_RegisteredIdentityIsAdvertisedAndResolves()
    {
        var provider = new Wave2EmbeddingProvider("embed-provider", "registered-embedding");
        await using var harness = await EmbeddingHarness.StartAsync(provider);

        ModelListResponse? catalog = await harness.Client.GetFromJsonAsync<ModelListResponse>(harness.Url("/v1/models"));
        Assert.NotNull(catalog);
        ModelCardDto card = Assert.Single(catalog.Data);
        Assert.Equal(provider.ModelId, card.Id);
        Assert.Equal(provider.ProviderId, card.OwnedBy);

        using HttpResponseMessage detail = await harness.Client.GetAsync(harness.Url($"/v1/models/{provider.ModelId}"));
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Embeddings_DuplicateRegisteredModelIdentity_FailsStartup()
    {
        var first = new Wave2EmbeddingProvider("embed-a", "duplicate-embedding");
        var second = new Wave2EmbeddingProvider("embed-b", "duplicate-embedding");
        var services = new ServiceCollection();
        services.AddSingleton<IEmbeddingProvider>(first);
        services.AddSingleton<IEmbeddingProvider>(second);
        services.AddSingleton<Claude4NetApiServer>();
        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        Claude4NetApiServer server = serviceProvider.GetRequiredService<Claude4NetApiServer>();

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.StartAsync(Wave2TestSupport.GetAvailablePort(), Wave2TestSupport.ApiKey));
            Assert.Contains("duplicate-embedding", error.Message);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void ProviderFactory_ApiSupportIsExplicit_AndRequestCreationHasNoDefaultImplementation()
    {
        PropertyInfo? supportProperty = typeof(IProviderFactory).GetProperty("SupportsApiRequests");
        MethodInfo? createMethod = typeof(IProviderFactory).GetMethod(nameof(IProviderFactory.CreateRequestProvider));

        Assert.NotNull(supportProperty);
        Assert.Equal(typeof(bool), supportProperty.PropertyType);
        Assert.NotNull(createMethod);
        Assert.Null(createMethod.GetMethodBody());
    }

    private sealed class EmbeddingHarness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private EmbeddingHarness(ServiceProvider services, Claude4NetApiServer server, HttpClient client, int port)
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

        public static async Task<EmbeddingHarness> StartAsync(IEmbeddingProvider provider)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IEmbeddingProvider>(provider);
            services.AddSingleton<Claude4NetApiServer>();
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            Claude4NetApiServer server = serviceProvider.GetRequiredService<Claude4NetApiServer>();
            int port = Wave2TestSupport.GetAvailablePort();
            await server.StartAsync(port, Wave2TestSupport.ApiKey);
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                Wave2TestSupport.ApiKey);
            return new EmbeddingHarness(serviceProvider, server, client, port);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Server.StopAsync();
            await _services.DisposeAsync();
        }
    }
}
