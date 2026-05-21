using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks; using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class GlobalConfig
    {
        public string Theme { get; set; } = "dark";
        public bool Verbose { get; set; } = false;
        public bool AutoCompactEnabled { get; set; } = true;
        public string? ActiveProvider { get; set; }
        public string? ActiveModel { get; set; }
    }

    public static class SettingsManager
    {
        private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude4net", "config.json");

        public static async Task<GlobalConfig> GetMergedSettingsAsync(string? workspaceDir = null)
        {
            var config = new GlobalConfig();

            // 1. User config ~/.claude4net/config.json
            string userConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude4net",
                "config.json"
            );
            if (File.Exists(userConfigPath))
            {
                MergeConfig(config, userConfigPath);
            }

            // 2. Workspace config .claude4net/config.json
            string wsConfigPath = Path.Combine(
                workspaceDir ?? (AppState.CurrentCwd ?? AppState.OriginalCwd),
                ".claude4net",
                "config.json"
            );
            if (File.Exists(wsConfigPath))
            {
                MergeConfig(config, wsConfigPath);
            }

            return config;
        }

        private static void MergeConfig(GlobalConfig config, string path)
        {
            string json = File.ReadAllText(path);
            var incoming = JsonSerializer.Deserialize<GlobalConfig>(json);
            if (incoming != null)
            {
                if (incoming.Theme != null) config.Theme = incoming.Theme;
                config.Verbose = incoming.Verbose;
                config.AutoCompactEnabled = incoming.AutoCompactEnabled; if (incoming.ActiveProvider != null) config.ActiveProvider = incoming.ActiveProvider; if (incoming.ActiveModel != null) config.ActiveModel = incoming.ActiveModel;
            }
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
