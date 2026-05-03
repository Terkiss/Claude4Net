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
/// Google Gemini API를 사용하여 텍스트 데이터의 임베딩 벡터를 생성하는 프로바이더입니다.
/// L1 캐시를 활용하여 동일한 텍스트에 대한 중복 계산을 방지합니다.
/// </summary>
public class GeminiEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private const string Model = "text-embedding-004";
    private readonly ConcurrentDictionary<string, float[]> _l1Cache = new();

    /// <summary>
    /// GeminiEmbeddingProvider의 새 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="httpClient">API 호출을 위한 클라이언트</param>
    public GeminiEmbeddingProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// 지정된 텍스트에 대한 임베딩 벡터를 비동기적으로 가져옵니다.
    /// </summary>
    /// <param name="text">임베딩을 생성할 텍스트</param>
    /// <param name="ct">작업 취소 토큰</param>
    /// <returns>생성된 임베딩(float 배열)</returns>
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<float>();

        // L1 메모리 캐시 확인
        if (_l1Cache.TryGetValue(text, out var cached)) return cached;

        // API 키 조회
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
        int j = 0;
        foreach (var val in embedding.EnumerateArray())
        {
            values[j++] = val.GetSingle();
        }

        // 캐시 업데이트
        _l1Cache[text] = values;

        return values;
    }
}
