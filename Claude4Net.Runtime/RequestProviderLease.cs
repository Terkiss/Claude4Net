using System;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime;

public sealed class RequestProviderLease : IAsyncDisposable
{
    private object? _ownedResource;

    public RequestProviderLease(ILLMProvider provider, IDisposable ownedResource)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
    }

    public RequestProviderLease(ILLMProvider provider, IAsyncDisposable ownedResource)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ownedResource = ownedResource ?? throw new ArgumentNullException(nameof(ownedResource));
    }

    private RequestProviderLease(ILLMProvider provider)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public ILLMProvider Provider { get; }

    public static RequestProviderLease NonOwning(ILLMProvider provider) => new(provider);

    public async ValueTask DisposeAsync()
    {
        object? ownedResource = Interlocked.Exchange(ref _ownedResource, null);
        if (ownedResource is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (ownedResource is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
