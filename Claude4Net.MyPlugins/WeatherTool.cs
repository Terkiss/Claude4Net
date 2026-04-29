using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.MyPlugins
{
    public class WeatherTool : ITool
    {
        public string Name => "weather_search";
        public string Description => "지정된 도시의 현재 날씨와 온도 정보를 검색합니다.";
        public bool IsConcurrencySafe => true;

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                location = new
                {
                    type = "string",
                    description = "검색할 도시의 영문 이름. 예: Seoul, Gwangju"
                }
            },
            required = new[] { "location" }
        };

        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<object> ExecuteAsync(string arguments, object context, System.Threading.CancellationToken ct = default)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var input = JsonSerializer.Deserialize<WeatherInput>(arguments, options)
                            ?? throw new ArgumentException("Invalid arguments for WeatherTool");

                if (string.IsNullOrWhiteSpace(input.location))
                {
                    return "Error: Location is required.";
                }

                string url = $"https://wttr.in/{Uri.EscapeDataString(input.location)}?format=4";
                string response = await _httpClient.GetStringAsync(url);

                return response.Trim();
            }
            catch (Exception ex)
            {
                return $"Error fetching weather: {ex.Message}";
            }
        }

        private class WeatherInput
        {
            public string location { get; set; } = string.Empty;
        }
    }
}
