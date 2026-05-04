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
    /// Anthropic API로부터 수신된 개별 이벤트 데이터를 나타내는 클래스입니다.
    /// </summary>
    public class AnthropicEvent
    {
        /// <summary>
        /// 이벤트 유형 (예: message_start, content_block_delta, message_stop 등)
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// 이벤트와 관련된 데이터 본문 (JSON 형식)
        /// </summary>
        public JsonElement Data { get; set; }
    }

    /// <summary>
    /// Anthropic Claude API와의 HTTP 통신 및 SSE 스트리밍 처리를 담당하는 저수준 클라이언트입니다.
    /// </summary>
    public class AnthropicClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        /// <summary>
        /// AnthropicClient의 새 인스턴스를 초기화합니다.
        /// </summary>
        /// <param name="httpClient">HTTP 요청을 처리할 HttpClient</param>
        /// <param name="apiKey">API 키 (null일 경우 환경 변수에서 조회)</param>
        /// <param name="baseUrl">API 엔드포인트 URL</param>
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
        /// Anthropic API에 메시지 스트림 생성을 요청하고 결과를 비동기적으로 열거합니다.
        /// </summary>
        /// <param name="payload">요청 페이로드 (model, messages, tools 등 포함)</param>
        /// <param name="ct">작업 취소 토큰</param>
        /// <returns>AnthropicEvent 객체의 비동기 스트림</returns>
        public async IAsyncEnumerable<AnthropicEvent> CreateMessageStreamAsync(object payload, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string json = JsonSerializer.Serialize(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v1/messages");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            // 환경 변수에서 API 키를 가져와 헤더에 추가
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

                // SSE 라인 파싱: event: 유형, data: 내용
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
