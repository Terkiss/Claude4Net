using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Claude4Net.SDK;

namespace Claude4Net.Api;

/// <summary>
/// Embedding provider that uses the Google Gemini API to generate vector embeddings for text inputs.
/// Implements an L1 in-memory cache to avoid redundant API calls for identical text inputs.
/// </summary>
public class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, float[]> _l1Cache = new();

    public string ProviderId => "gemini";
    public string ModelId => "text-embedding-004";

    /// <summary>
    /// Initializes a new instance of the <see cref="GeminiEmbeddingProvider"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used for API calls.</param>
    public GeminiEmbeddingProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Asynchronously generates an embedding vector for the given text input.
    /// Returns a cached result if the same text was previously embedded.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="ct">Cancellation token to abort the operation.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

        // Check the L1 in-memory cache for a previously computed embedding
        if (_l1Cache.TryGetValue(text, out var cached)) return cached;

        // Retrieve the API key from the authentication manager
        string? apiKey = AuthManager.GetApiKey("gemini");
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Gemini API key not found.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:embedContent?key={apiKey}";
        
        var request = new
        {
            model = $"models/{ModelId}",
            content = new { parts = new[] { new { text = text } } }
        };

        using var response = await _httpClient.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var embedding = result.GetProperty("embedding").GetProperty("values");

        float[] values = new float[embedding.GetArrayLength()];
        int j = 0;
        foreach (var val in embedding.EnumerateArray())
        {
            values[j++] = val.GetSingle();
        }

        // Store the computed embedding in the L1 cache
        _l1Cache[text] = values;

        return values;
    }
}
