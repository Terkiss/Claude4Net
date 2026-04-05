using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Claude4Net.Runtime
{
    public class GlobalConfig
    {
        public string Theme { get; set; } = "dark";
        public bool Verbose { get; set; } = false;
        public bool AutoCompactEnabled { get; set; } = true;
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude4net", "config.json");

        public static async Task<GlobalConfig> GetMergedSettingsAsync()
        {
            if (!File.Exists(ConfigPath)) return new GlobalConfig();
            string json = await File.ReadAllTextAsync(ConfigPath);
            return JsonSerializer.Deserialize<GlobalConfig>(json) ?? new GlobalConfig();
        }

        public static async Task SaveGlobalConfigAsync(GlobalConfig config)
        {
            string? dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ConfigPath, json);
        }
    }
}
