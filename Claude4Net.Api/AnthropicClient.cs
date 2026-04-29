using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Api
{
    public class AnthropicEvent
    {
        public string Type { get; set; } = string.Empty;
        public JsonElement Data { get; set; }
    }

    public class AnthropicClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AnthropicClient(HttpClient httpClient, string? apiKey = null, string? baseUrl = null)
        {
            _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com";
            _httpClient = httpClient;
            
            if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
            {
                _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Claude4Net/0.1.0");
            }
        }

        public async IAsyncEnumerable<AnthropicEvent> CreateMessageStreamAsync(object payload, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            // Get API Key from Environment for now
            string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (!string.IsNullOrEmpty(apiKey)) request.Headers.Add("x-api-key", apiKey);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new System.IO.StreamReader(stream);

            string? currentEventType = null;
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("event:")) currentEventType = line.Substring(6).Trim();
                else if (line.StartsWith("data:") && currentEventType != null)
                {
                    string dataJson = line.Substring(5).Trim();
                    if (dataJson == "[DONE]") break;
                    var data = JsonSerializer.Deserialize<JsonElement>(dataJson);
                    yield return new AnthropicEvent { Type = currentEventType, Data = data };
                }
            }
        }
    }
}
