using System.Net;
using System.Text;
using Claude4Net.Api;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests;

public sealed class ProviderResourceOwnershipTests
{
    [Fact]
    public async Task AnthropicClient_DisposesRequestAndResponse_AfterStreamCompletion()
    {
        var handler = new TrackingHandler(_ => "event: message_stop\ndata: {}\n\n");
        using var httpClient = new HttpClient(handler);
        var client = new AnthropicClient(httpClient, baseUrl: "https://example.test");

        await foreach (AnthropicEvent _ in client.CreateMessageStreamAsync(new { model = "test" }))
        {
        }

        AssertSingleDisposal(handler.Exchanges.Single());
    }

    [Fact]
    public async Task GeminiEmbeddingProvider_DisposesResponse_AfterSuccessfulRead()
    {
        const string environmentVariable = "GEMINI_API_KEY";
        string? originalApiKey = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, "test-key");
        try
        {
            var handler = new TrackingHandler(_ => "{\"embedding\":{\"values\":[0.25,0.75]}}");
            using var httpClient = new HttpClient(handler);
            var provider = new GeminiEmbeddingProvider(httpClient);

            float[] vector = await provider.GetEmbeddingAsync("ownership-test");

            Assert.Equal(new[] { 0.25f, 0.75f }, vector);
            TrackingExchange exchange = Assert.Single(handler.Exchanges);
            Assert.Equal(1, exchange.Response.DisposeCount);
            Assert.Equal(1, exchange.ResponseContent.DisposeCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalApiKey);
        }
    }

    [Fact]
    public async Task OpenAiCompatProvider_DisposesRequestAndResponse_AfterStreamCompletion()
    {
        var handler = new TrackingHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/embeddings", StringComparison.Ordinal)
                ? "{\"data\":[{\"embedding\":[0.25,0.75]}]}"
                : "data: [DONE]\n\n");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatProvider(httpClient, EmptyToolRegistry.Instance, CreateOpenAiDescriptor());

        await foreach (LLMStreamEvent _ in provider.StreamQueryAsync("ownership-test"))
        {
        }
        List<float[]> vectors = await provider.GetEmbeddingsAsync(new[] { "ownership-test" });

        Assert.Equal(new[] { 0.25f, 0.75f }, Assert.Single(vectors));
        Assert.Equal(2, handler.Exchanges.Count);
        Assert.All(handler.Exchanges, AssertSingleDisposal);
    }

    [Fact]
    public async Task OpenAiCompatProvider_ListModelsAsync_DisposesRequestAndResponse()
    {
        var handler = new TrackingHandler(_ => "{\"data\":[{\"id\":\"model-a\"}]}");
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatProvider(httpClient, EmptyToolRegistry.Instance, CreateOpenAiDescriptor());

        List<string> models = await provider.ListModelsAsync();

        Assert.Equal(new[] { "model-a" }, models);
        AssertSingleDisposal(Assert.Single(handler.Exchanges));
    }

    [Fact]
    public async Task GlmProvider_DisposesRequestsAndResponses_AfterCompletedReads()
    {
        var handler = new TrackingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/models", StringComparison.Ordinal) =>
                "{\"data\":[{\"id\":\"glm-test\"}]}",
            var path when path.EndsWith("/embeddings", StringComparison.Ordinal) =>
                "{\"data\":[{\"embedding\":[0.25,0.75]}]}",
            _ => "data: [DONE]\n\n"
        });
        using var httpClient = new HttpClient(handler);
        var provider = new GlmProvider(httpClient, EmptyToolRegistry.Instance);

        await provider.ListModelsAsync();
        await foreach (LLMStreamEvent _ in provider.StreamQueryAsync("ownership-test"))
        {
        }
        await provider.GetEmbeddingsAsync(new[] { "ownership-test" });

        Assert.Equal(3, handler.Exchanges.Count);
        Assert.All(handler.Exchanges, AssertSingleDisposal);
    }

    [Fact]
    public async Task OllamaProvider_StreamQueryAsync_DisposesRequestAndResponse()
    {
        const string environmentVariable = "OLLAMA_API_KEY";
        string? originalEndpoint = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, "http://127.0.0.1:11434");
        try
        {
            var handler = new TrackingHandler(_ => "{\"message\":{\"content\":\"Done\"},\"done\":true}\n");
            using var httpClient = new HttpClient(handler);
            var provider = new OllamaProvider(httpClient, EmptyToolRegistry.Instance);

            await ConsumeOllamaChatAsync(provider);

            AssertSingleDisposal(Assert.Single(handler.Exchanges));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalEndpoint);
        }
    }

    [Fact]
    public async Task OllamaProvider_StreamQueryAsync_RejectsRemoteHttpBeforeSend()
    {
        const string environmentVariable = "OLLAMA_API_KEY";
        string? originalEndpoint = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, "http://192.0.2.1:11434");
        try
        {
            var handler = new TrackingHandler(_ => "{\"message\":{},\"done\":true}\n");
            using var httpClient = new HttpClient(handler);
            var provider = new OllamaProvider(httpClient, EmptyToolRegistry.Instance);

            await Assert.ThrowsAsync<ArgumentException>(() => ConsumeOllamaChatAsync(provider));
            Assert.Empty(handler.Exchanges);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalEndpoint);
        }
    }

    [Theory]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://localhost:11434")]
    [InlineData("http://[::1]:11434")]
    [InlineData("https://ollama.example.test")]
    public async Task OllamaProvider_StreamQueryAsync_AllowsSecureOrLoopbackEndpoint(string endpoint)
    {
        const string environmentVariable = "OLLAMA_API_KEY";
        string? originalEndpoint = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, endpoint);
        try
        {
            var handler = new TrackingHandler(_ => "{\"message\":{},\"done\":true}\n");
            using var httpClient = new HttpClient(handler);
            var provider = new OllamaProvider(httpClient, EmptyToolRegistry.Instance);

            await ConsumeOllamaChatAsync(provider);

            Assert.Single(handler.Exchanges);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, originalEndpoint);
        }
    }

    private static async Task ConsumeOllamaChatAsync(OllamaProvider provider)
    {
        await foreach (LLMStreamEvent _ in provider.StreamQueryAsync("endpoint-policy-test", "test-model"))
        {
        }
    }

    private static ProviderDescriptor CreateOpenAiDescriptor() => new()
    {
        Id = "ownership-openai",
        Label = "Ownership OpenAI",
        TransportKind = "openai-compat",
        Endpoint = "https://example.test/v1",
        DefaultModels = new ProviderDefaultModels { Small = "embedding-model", Large = "chat-model" },
        Auth = new ProviderAuth { Mode = "none" }
    };

    private static void AssertSingleDisposal(TrackingExchange exchange)
    {
        Assert.Equal(1, exchange.RequestContent.DisposeCount);
        Assert.Equal(1, exchange.Response.DisposeCount);
        Assert.Equal(1, exchange.ResponseContent.DisposeCount);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string> _responseBody;

        public TrackingHandler(Func<HttpRequestMessage, string> responseBody)
        {
            _responseBody = responseBody;
        }

        public List<TrackingExchange> Exchanges { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpContent? originalContent = request.Content;
            var requestContent = new TrackingContent("{}");
            request.Content = requestContent;
            originalContent?.Dispose();

            var responseContent = new TrackingContent(_responseBody(request));
            var response = new TrackingResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = responseContent
            };
            Exchanges.Add(new TrackingExchange(requestContent, response, responseContent));
            return Task.FromResult<HttpResponseMessage>(response);
        }
    }

    private sealed record TrackingExchange(
        TrackingContent RequestContent,
        TrackingResponseMessage Response,
        TrackingContent ResponseContent);

    private sealed class TrackingResponseMessage : HttpResponseMessage
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

    private sealed class TrackingContent : HttpContent
    {
        private readonly byte[] _payload;

        public TrackingContent(string payload)
        {
            _payload = Encoding.UTF8.GetBytes(payload);
        }

        public int DisposeCount { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(_payload).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = _payload.Length;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
            }
            base.Dispose(disposing);
        }
    }
}
