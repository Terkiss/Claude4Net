using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api;

public class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private const string Model = "text-embedding-004";

    public GeminiEmbeddingProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        string? apiKey = AuthManager.GetApiKey("gemini");
        if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Gemini API key not found.");

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:embedContent?key={apiKey}";
        
        var request = new
        {
            model = $"models/{Model}",
            content = new { parts = new[] { new { text = text } } }
        };

        var response = await _httpClient.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var embedding = result.GetProperty("embedding").GetProperty("values");

        float[] values = new float[embedding.GetArrayLength()];
        int i = 0;
        foreach (var val in embedding.EnumerateArray())
        {
            values[i++] = val.GetSingle();
        }

        return values;
    }
}
