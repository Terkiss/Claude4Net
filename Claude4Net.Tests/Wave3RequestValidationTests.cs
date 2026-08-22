using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests;

[Collection("AppState")]
public sealed class Wave3RequestValidationTests : IAsyncLifetime
{
    private const string ApiKey = "wave3-validation-key";
    private readonly HttpClient _client = new();
    private readonly Wave3TrackingFactory _factory = new();
    private readonly Wave2EmbeddingProvider _embeddingProvider = new("embedding", "text-embedding-004");
    private ServiceProvider _services = null!;
    private Claude4NetApiServer _server = null!;
    private string _originalProvider = null!;
    private string _originalModel = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        _originalProvider = AppState.ActiveProvider;
        _originalModel = AppState.ActiveModel;
        AppState.ActiveProvider = "wave3";
        AppState.ActiveModel = "wave3-model";

        var registry = new ProviderRegistry();
        registry.Register(Wave2TestSupport.Descriptor("wave3", "wave3", "wave3-model"));
        registry.RegisterFactory(_factory);

        var services = new ServiceCollection();
        services.AddSingleton(registry);
        services.AddSingleton<IEmbeddingProvider>(_embeddingProvider);
        services.AddSingleton<Claude4NetApiServer>();
        _services = services.BuildServiceProvider();
        _server = _services.GetRequiredService<Claude4NetApiServer>();
        _port = Wave2TestSupport.GetAvailablePort();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        await _server.StartAsync(_port, ApiKey);
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _client.Dispose();
        await _services.DisposeAsync();
        AppState.ActiveProvider = _originalProvider;
        AppState.ActiveModel = _originalModel;
    }

    public static TheoryData<string> MalformedChatPayloads => new()
    {
        """{"model":"wave3-model","messages":null}""",
        """{"model":"wave3-model","messages":[]}""",
        """{"model":"wave3-model","messages":[null]}""",
        """{"model":"wave3-model","messages":[{"role":null,"content":"hello"}]}""",
        """{"model":"wave3-model","messages":[{"role":"developer","content":"hello"}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":null}]}""",
        """{"model":"wave3-model","messages":[{"role":"assistant","content":null}]}""",
        """{"model":"wave3-model","messages":[{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","function":null}]}]}""",
        """{"model":"wave3-model","messages":[{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","function":{"name":null,"arguments":"{}"}}]}]}""",
        """{"model":"wave3-model","messages":[{"role":"assistant","content":null,"tool_calls":[{"id":"call-1","function":{"name":"declared","arguments":null}}]}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"tools":[{"type":"function","function":null}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"tools":[{"type":"function","function":{"name":"   "}}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"tools":[{"type":"function","function":{"name":"same"}},{"type":"function","function":{"name":"same"}}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"tools":[{"type":"function","function":{"name":"x","parameters":[]}}]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":[]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":null}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":[""]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":["ok",null]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":["ok",1]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":{}}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":1}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":["1","2","3","4","5"]}""",
        """{"model":"wave3-model","messages":[{"role":"user","content":"hello"}],"stop":""}""",
        """{"model":"   ","messages":[{"role":"user","content":"hello"}]}"""
    };

    [Theory]
    [MemberData(nameof(MalformedChatPayloads))]
    public async Task ChatCompletions_MalformedKnownFields_Return400BeforeProviderCreation(string payload)
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/chat/completions", payload);

        await AssertInvalidRequestAsync(response);
        Assert.Equal(0, _factory.RequestCreateCount);
    }

    public static TheoryData<string> MalformedLegacyPayloads => new()
    {
        """{"model":"wave3-model","prompt":123}""",
        """{"model":"wave3-model","prompt":{}}""",
        """{"model":"wave3-model","prompt":[]}""",
        """{"model":"wave3-model","prompt":["ok",1]}""",
        """{"model":"wave3-model","prompt":null}""",
        """{"model":"wave3-model","prompt":"hello","stop":[null]}"""
    };

    [Theory]
    [MemberData(nameof(MalformedLegacyPayloads))]
    public async Task TextCompletions_MalformedKnownFields_Return400BeforeProviderCreation(string payload)
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/completions", payload);

        await AssertInvalidRequestAsync(response);
        Assert.Equal(0, _factory.RequestCreateCount);
    }

    public static TheoryData<string> MalformedEmbeddingPayloads => new()
    {
        """{"model":"text-embedding-004","input":123}""",
        """{"model":"text-embedding-004","input":{}}""",
        """{"model":"text-embedding-004","input":[]}""",
        """{"model":"text-embedding-004","input":["ok",1]}""",
        """{"model":"text-embedding-004","input":["ok",null]}""",
        """{"model":"text-embedding-004","input":null}""",
        """{"model":"text-embedding-004","input":"hello","dimensions":0}""",
        """{"model":"text-embedding-004","input":"hello","dimensions":-1}"""
    };

    [Theory]
    [MemberData(nameof(MalformedEmbeddingPayloads))]
    public async Task Embeddings_MalformedKnownFields_Return400BeforeProviderUse(string payload)
    {
        using HttpResponseMessage response = await PostJsonAsync("/v1/embeddings", payload);

        await AssertInvalidRequestAsync(response);
        Assert.Equal(0, _embeddingProvider.CallCount);
        Assert.Equal(0, _factory.RequestCreateCount);
    }

    [Fact]
    public async Task Embeddings_MoreThan256Inputs_Returns400BeforeProviderUse()
    {
        string payload = JsonSerializer.Serialize(new
        {
            model = "text-embedding-004",
            input = Enumerable.Repeat("x", 257)
        });

        using HttpResponseMessage response = await PostJsonAsync("/v1/embeddings", payload);

        await AssertInvalidRequestAsync(response, "input");
        Assert.Equal(0, _embeddingProvider.CallCount);
    }

    [Fact]
    public async Task Embeddings_InputOver32768Utf16Units_Returns400BeforeProviderUse()
    {
        string payload = JsonSerializer.Serialize(new
        {
            model = "text-embedding-004",
            input = new[] { new string('x', 32_769) }
        });

        using HttpResponseMessage response = await PostJsonAsync("/v1/embeddings", payload);

        await AssertInvalidRequestAsync(response, "input");
        Assert.Equal(0, _embeddingProvider.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\r\n")]
    public async Task Embeddings_EmptyOrWhitespaceArrayItem_Returns400BeforeProviderUse(string invalidInput)
    {
        string payload = JsonSerializer.Serialize(new
        {
            model = "text-embedding-004",
            input = new[] { "valid", invalidInput }
        });

        using HttpResponseMessage response = await PostJsonAsync("/v1/embeddings", payload);

        await AssertInvalidRequestAsync(response, "input");
        Assert.Equal(0, _embeddingProvider.CallCount);
    }

    [Fact]
    public async Task Embeddings_ExactInputLimits_AreAccepted()
    {
        string[] inputs = Enumerable.Repeat("x", 255)
            .Append(new string('x', 32_768))
            .ToArray();
        string payload = JsonSerializer.Serialize(new
        {
            model = "text-embedding-004",
            input = inputs
        });

        using HttpResponseMessage response = await PostJsonAsync("/v1/embeddings", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(256, _embeddingProvider.CallCount);
    }

    [Fact]
    public async Task ChatCompletions_UnknownProperties_RemainAccepted()
    {
        const string payload = """
            {"model":"wave3-model","messages":[{"role":"user","content":"hello","future_message_field":true}],"future_request_field":{"enabled":true}}
            """;

        using HttpResponseMessage response = await PostJsonAsync("/v1/chat/completions", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _factory.RequestCreateCount);
    }

    private Task<HttpResponseMessage> PostJsonAsync(string path, string payload) =>
        _client.PostAsync(
            $"http://localhost:{_port}{path}",
            new StringContent(payload, Encoding.UTF8, "application/json"));

    private static async Task AssertInvalidRequestAsync(HttpResponseMessage response, string? parameter = null)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("invalid_request_error", document.RootElement.GetProperty("error").GetProperty("type").GetString());
        if (parameter is not null)
        {
            Assert.Equal(parameter, document.RootElement.GetProperty("error").GetProperty("param").GetString());
        }
    }
}

internal sealed class Wave3TrackingFactory : IProviderFactory
{
    private int _requestCreateCount;

    public bool SupportsApiRequests => true;
    public int RequestCreateCount => Volatile.Read(ref _requestCreateCount);
    public bool CanCreate(ProviderDescriptor descriptor) => descriptor.TransportKind == "wave3";
    public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider) => new Wave2EchoProvider();

    public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
    {
        Interlocked.Increment(ref _requestCreateCount);
        return new Wave2EchoProvider();
    }
}
