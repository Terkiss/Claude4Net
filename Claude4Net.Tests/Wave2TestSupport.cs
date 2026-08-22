using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests;

internal static class Wave2TestSupport
{
    public const string ApiKey = "wave2-routing-test-key";

    public static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static ProviderDescriptor Descriptor(
        string providerId,
        string transportKind,
        string smallModel,
        string? largeModel = null) => new()
        {
            Id = providerId,
            Label = providerId,
            TransportKind = transportKind,
            DefaultModels = new ProviderDefaultModels
            {
                Small = smallModel,
                Large = largeModel ?? smallModel
            }
        };

    public static ProviderRegistry CreateOfficialSdkRegistry(string transportKind)
    {
        var registry = new ProviderRegistry();
        registry.Register(Descriptor(
            "anthropic",
            transportKind,
            "claude-3-5-sonnet",
            "claude-3-5-sonnet-20241022"));
        registry.Register(Descriptor("openai", transportKind, "gpt-4o"));
        registry.Register(Descriptor("google", transportKind, "gemini-2.5-flash"));
        return registry;
    }
}

internal sealed class Wave2TrackingFactory : IProviderFactory
{
    private readonly string _transportKind;
    private int _requestCreateCount;

    public Wave2TrackingFactory(string transportKind, bool supportsApiRequests = true)
    {
        _transportKind = transportKind;
        SupportsApiRequests = supportsApiRequests;
    }

    public bool SupportsApiRequests { get; }
    public int RequestCreateCount => Volatile.Read(ref _requestCreateCount);
    public bool CanCreate(ProviderDescriptor descriptor) => descriptor.TransportKind == _transportKind;
    public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider) => new Wave2EchoProvider();

    public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
    {
        if (!SupportsApiRequests)
        {
            throw new NotSupportedException($"Provider transport '{_transportKind}' cannot serve HTTP API requests.");
        }

        Interlocked.Increment(ref _requestCreateCount);
        return new Wave2EchoProvider();
    }
}

internal sealed class Wave2EchoProvider : ILLMProvider
{
    public string Name => "wave2-echo";
    public ITokenCounter TokenCounter { get; } = new Wave2TokenCounter();
    public int ContextLimit => 4096;

    public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
        string prompt,
        string? model = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = $"{model}:{prompt}" };
    }

    public void AddMessage(object message) { }
    public IReadOnlyList<object> GetHistory() => Array.Empty<object>();
    public void SetHistory(IEnumerable<object> history) { }
}

internal sealed class Wave2TokenCounter : ITokenCounter
{
    public int CountTokens(string text) => Math.Max(1, text.Length / 4);
    public int CountTokens(object message) => 1;
    public int CountTokens(IEnumerable<object> history) => 1;
}

internal sealed class Wave2EmbeddingProvider : IEmbeddingProvider
{
    private int _callCount;

    public Wave2EmbeddingProvider(string providerId, string modelId, float[]? vector = null)
    {
        ProviderId = providerId;
        ModelId = modelId;
        Vector = vector ?? new[] { 0.25f, -0.5f, 0.75f };
    }

    public string ProviderId { get; }
    public string ModelId { get; }
    public float[] Vector { get; }
    public int CallCount => Volatile.Read(ref _callCount);

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _callCount);
        return Task.FromResult(Vector);
    }
}
