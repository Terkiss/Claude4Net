using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Claude4Net.SDK; // AuthManager 등에서 API KEY를 가져올 경우 사용

namespace Claude4Net.Tools
{
    public class ImageEngineInput
    {
        public string prompt { get; set; } = string.Empty;
        public string? image { get; set; }
    }

    public class ImageEngineTool : ITool
    {
        public string Name => "ImageEngineTool";
        public string Description => "Generate or edit an image. Always save image logic is included.";
        public List<string>? Aliases => new() { "image", "img" };

        public object? InputSchema => new
        {
            type = "object",
            properties = new
            {
                prompt = new { type = "string" }
            },
            required = new[] { "prompt" }
        };

        public async Task<object> ExecuteAsync(string arguments, object context)
        {
            // 1. 파라미터 역직렬화
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<ImageEngineInput>(arguments, options)
                        ?? throw new ArgumentException("Invalid arguments");

            // 2. 나노 바나나 기본 프롬프트 믹싱
            string finalPrompt = input.prompt + " (Apply nano banana style / theme explicitly)";

            // 3. API 키 획득 빛 Endpoint 정의 (Claude4Net 구조상 AuthManager.cs 활용 권장)
            // AuthManager.GetGeminiApiKey() 같은 메서드가 있다면 대체하세요!
            string? apiKey = AuthManager.GetGeminiApiKey();

            if (string.IsNullOrEmpty(apiKey)) throw new Exception("API Key is not found.");


            string url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-image-preview:generateContent?key={apiKey}";

            // 4. 요청 페이로드 세팅 (Python의 contents=[prompt] 와 동일한 JSON 체계)
            var payload = new
            {
                contents = new[] {
                    new { parts = new[] { new { text = finalPrompt } } }
                }
            };

            // 5. HttpClient로 백엔드 전송
            using var client = new HttpClient();
            var response = await client.PostAsJsonAsync(url, payload);
            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Gemini API Error: {error}");
            }

            // 6. 결과 파싱 및 이미지 Base64 찾기
            var data = await response.Content.ReadFromJsonAsync<JsonElement>();
            string base64Image = null;

            if (data.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                if (content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        // 파이썬의: elif part.inline_data is not None:
                        if (part.TryGetProperty("inlineData", out var inlineData))
                        {
                            base64Image = inlineData.GetProperty("data").GetString();
                            break;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(base64Image))
                throw new Exception("결과물에서 이미지를 추출하지 못했습니다.");

            // 7. 디코딩 후 물리 파일로 저장
            string savePath = "generated_image.png";
            byte[] imageBytes = Convert.FromBase64String(base64Image);
            await File.WriteAllBytesAsync(savePath, imageBytes);

            return new
            {
                status = "Success",
                message = "The image has been successfully generated.",
                savedPath = savePath,
                appliedPrompt = finalPrompt
            };
        }
    }
}
