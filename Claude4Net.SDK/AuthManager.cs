using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    /// <summary>
    /// API 키 및 인증 정보를 관리하는 정적 클래스입니다.
    /// </summary>
    public static class AuthManager
    {
        private static readonly string KeyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "api_key.json");

        /// <summary>
        /// 파일에서 API 키 목록을 로드합니다.
        /// </summary>
        private static Dictionary<string, string> LoadKeys()
        {
            if (!File.Exists(KeyFilePath)) return new Dictionary<string, string>();
            try
            {
                // 로컬 파일에서 JSON 형식의 키 맵을 읽어옴
                string json = File.ReadAllText(KeyFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); } // 로드 실패 시 빈 딕셔너리 반환
        }

        /// <summary>
        /// 특정 제공자의 API 키를 가져옵니다. 로컬 파일과 환경 변수를 모두 확인합니다.
        /// </summary>
        /// <param name="provider">제공자 이름 (예: gemini, claude)</param>
        /// <returns>찾은 API 키 문자열, 없을 경우 null</returns>
        public static string? GetApiKey(string provider)
        {
            var keys = LoadKeys();
            // 1. 로컬 파일 확인
            if (keys.TryGetValue(provider.ToLower(), out var key)) return key;
            
            // 2. 환경 변수 확인 (PROVIDER_API_KEY 형식)
            return Environment.GetEnvironmentVariable($"{provider.ToUpper()}_API_KEY");
        }

        /// <summary> Anthropic(Claude) API 키를 가져옵니다. </summary>
        public static string? GetAnthropicApiKey() => GetApiKey("claude");
        /// <summary> Google Gemini API 키를 가져옵니다. </summary>
        public static string? GetGeminiApiKey() => GetApiKey("gemini");
        /// <summary> Discord 봇 토큰을 가져옵니다. </summary>
        public static string? GetDiscordApiKey() => GetApiKey("discord");

        /// <summary>
        /// 특정 제공자의 API 키를 로컬 파일에 저장하고 현재 환경 변수에도 설정합니다.
        /// </summary>
        /// <param name="provider">제공자 이름</param>
        /// <param name="key">저장할 키 값</param>
        public static async Task SaveProviderKeyAsync(string provider, string key)
        {
            var keys = LoadKeys();
            keys[provider.ToLower()] = key;
            
            // JSON으로 직렬화하여 파일에 저장
            string json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(KeyFilePath, json);
            
            // 현재 프로세스의 환경 변수에도 즉시 반영
            Environment.SetEnvironmentVariable($"{provider.ToUpper()}_API_KEY", key);
        }
    }
}
