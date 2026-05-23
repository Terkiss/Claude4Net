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

        internal static Func<string> UserConfigPathResolver { get; set; } = () => ConfigPath;

        public static async Task<GlobalConfig> GetMergedSettingsAsync(string? workspaceDir = null)
        {
            var config = new GlobalConfig();

            // 1. User config ~/.claude4net/config.json
            string userConfigPath = UserConfigPathResolver();
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
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("Theme", out _) || root.TryGetProperty("theme", out _))
                {
                    if (incoming.Theme != null) config.Theme = incoming.Theme;
                }

                if (root.TryGetProperty("Verbose", out _) || root.TryGetProperty("verbose", out _))
                {
                    config.Verbose = incoming.Verbose;
                }

                if (root.TryGetProperty("AutoCompactEnabled", out _) || root.TryGetProperty("autoCompactEnabled", out _))
                {
                    config.AutoCompactEnabled = incoming.AutoCompactEnabled;
                }

                if (incoming.ActiveProvider != null) config.ActiveProvider = incoming.ActiveProvider;
                if (incoming.ActiveModel != null) config.ActiveModel = incoming.ActiveModel;
            }
        }

        public static async Task SaveGlobalConfigAsync(GlobalConfig config)
        {
            string configPath = UserConfigPathResolver();
            string? dir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json);
        }

        public static void ApplyPrecedence(GlobalConfig config, string? cliProvider, string? cliModel)
        {
            // Determine active provider
            string? provider = null;
            bool providerIsExplicit = false;

            // 1. Config level (User config, Workspace config)
            if (!string.IsNullOrWhiteSpace(config.ActiveProvider))
            {
                provider = config.ActiveProvider;
                providerIsExplicit = true;
            }

            // 2. Environment variables: CLAUDE4NET_ACTIVE_PROVIDER or CLAUDE4NET_PROVIDER
            string? envProvider = Environment.GetEnvironmentVariable("CLAUDE4NET_ACTIVE_PROVIDER")
                ?? Environment.GetEnvironmentVariable("CLAUDE4NET_PROVIDER");
            if (!string.IsNullOrWhiteSpace(envProvider))
            {
                provider = envProvider;
                providerIsExplicit = true;
            }

            // 3. CLI arguments
            if (!string.IsNullOrWhiteSpace(cliProvider))
            {
                provider = cliProvider;
                providerIsExplicit = true;
            }

            if (provider != null)
            {
                AppState.ActiveProvider = provider;
                AppState.IsProviderExplicitlySet = providerIsExplicit;
            }
            else
            {
                AppState.ActiveProvider = "gemini";
                AppState.IsProviderExplicitlySet = false;
            }

            // Determine active model
            string? model = null;

            // 1. Config level
            if (!string.IsNullOrWhiteSpace(config.ActiveModel))
            {
                model = config.ActiveModel;
            }

            // 2. Environment variables: CLAUDE4NET_ACTIVE_MODEL or CLAUDE4NET_MODEL
            string? envModel = Environment.GetEnvironmentVariable("CLAUDE4NET_ACTIVE_MODEL")
                ?? Environment.GetEnvironmentVariable("CLAUDE4NET_MODEL");
            if (!string.IsNullOrWhiteSpace(envModel))
            {
                model = envModel;
            }

            // 3. CLI arguments
            if (!string.IsNullOrWhiteSpace(cliModel))
            {
                model = cliModel;
            }

            if (model != null)
            {
                AppState.ActiveModel = model;
            }
            else
            {
                AppState.ActiveModel = "gemini-3.1-flash-lite-preview"; // Default model
            }
        }
    }
}
