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
    /// <summary>
    /// Represents a single event received from the Anthropic streaming API.
    /// Each event contains a type identifier and its associated data payload.
    /// </summary>
    public class AnthropicEvent
    {
        /// <summary>
        /// Gets or sets the event type identifier (e.g., message_start, content_block_delta, message_stop).
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the event data payload in JSON format.
        /// </summary>
        public JsonElement Data { get; set; }
    }

    /// <summary>
    /// Low-level HTTP client responsible for communicating with the Anthropic Claude API.
    /// Handles Server-Sent Events (SSE) streaming for real-time message generation.
    /// </summary>
    public class AnthropicClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        /// <summary>
        /// Initializes a new instance of the <see cref="AnthropicClient"/> class.
        /// </summary>
        /// <param name="httpClient">The HTTP client used for sending requests.</param>
        /// <param name="apiKey">Optional API key. If null, the key is retrieved from the ANTHROPIC_API_KEY environment variable.</param>
        /// <param name="baseUrl">Optional API base URL. Defaults to the ANTHROPIC_BASE_URL environment variable or https://api.anthropic.com.</param>
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

        /// <summary>
        /// Sends a message creation request to the Anthropic API and asynchronously enumerates the streamed events.
        /// Uses SSE (Server-Sent Events) protocol to receive incremental response chunks.
        /// </summary>
        /// <param name="payload">The request payload containing model, messages, tools, and other parameters.</param>
        /// <param name="ct">Cancellation token to abort the streaming operation.</param>
        /// <returns>An asynchronous stream of <see cref="AnthropicEvent"/> objects.</returns>
        public async IAsyncEnumerable<AnthropicEvent> CreateMessageStreamAsync(object payload, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            // Retrieve the API key from environment variables and attach it to the request header
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

                // Parse SSE lines: "event:" for event type, "data:" for payload content
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
