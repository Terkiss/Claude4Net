using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Claude4Net.SDK
{
    public static class AuthManager
    {
        private static readonly string KeyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "api_key.json");

        private static Dictionary<string, string> LoadKeys()
        {
            if (!File.Exists(KeyFilePath)) return new Dictionary<string, string>();
            try
            {
                string json = File.ReadAllText(KeyFilePath);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        public static string? GetApiKey(string provider)
        {
            var keys = LoadKeys();
            return keys.TryGetValue(provider.ToLower(), out var key) ? key : Environment.GetEnvironmentVariable($"{provider.ToUpper()}_API_KEY");
        }

        public static string? GetAnthropicApiKey() => GetApiKey("claude");
        public static string? GetGeminiApiKey() => GetApiKey("gemini");

        public static async Task SaveProviderKeyAsync(string provider, string key)
        {
            var keys = LoadKeys();
            keys[provider.ToLower()] = key;
            string json = JsonSerializer.Serialize(keys, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(KeyFilePath, json);
            Environment.SetEnvironmentVariable($"{provider.ToUpper()}_API_KEY", key);
        }
    }
}
