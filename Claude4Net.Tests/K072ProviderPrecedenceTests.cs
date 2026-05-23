using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K072ProviderPrecedenceTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _originalSystemBaseDir;
        private readonly Func<string> _originalUserProvidersDirResolver;
        private readonly Func<string> _originalUserConfigPathResolver;
        private readonly string _originalActiveProvider;
        private readonly string _originalActiveModel;
        private readonly bool _originalIsProviderExplicitlySet;

        public K072ProviderPrecedenceTests()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Claude4Net_K072_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            _originalSystemBaseDir = AppState.SystemBaseDir;
            _originalUserProvidersDirResolver = ProviderRegistry.UserProvidersDirResolver;
            _originalUserConfigPathResolver = SettingsManager.UserConfigPathResolver;
            _originalActiveProvider = AppState.ActiveProvider;
            _originalActiveModel = AppState.ActiveModel;
            _originalIsProviderExplicitlySet = AppState.IsProviderExplicitlySet;
        }

        public void Dispose()
        {
            AppState.SystemBaseDir = _originalSystemBaseDir;
            ProviderRegistry.UserProvidersDirResolver = _originalUserProvidersDirResolver;
            SettingsManager.UserConfigPathResolver = _originalUserConfigPathResolver;
            AppState.ActiveProvider = _originalActiveProvider;
            AppState.ActiveModel = _originalActiveModel;
            AppState.IsProviderExplicitlySet = _originalIsProviderExplicitlySet;

            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_PROVIDER", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_PROVIDER", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_MODEL", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_MODEL", null);

            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [Fact]
        public void ProviderRegistry_ShouldLoadDescriptorsInPrecedenceOrder()
        {
            // Setup directory structure
            var systemDir = Path.Combine(_tempRoot, "system");
            var systemProvidersDir = Path.Combine(systemDir, "providers");
            Directory.CreateDirectory(systemProvidersDir);

            var userDir = Path.Combine(_tempRoot, "user");
            var userProvidersDir = Path.Combine(userDir, "providers");
            Directory.CreateDirectory(userProvidersDir);

            var workspaceDir = Path.Combine(_tempRoot, "workspace");
            var workspaceProvidersDir = Path.Combine(workspaceDir, ".claude4net", "providers");
            Directory.CreateDirectory(workspaceProvidersDir);

            // Configure resolvers
            AppState.SystemBaseDir = systemDir;
            ProviderRegistry.UserProvidersDirResolver = () => userProvidersDir;

            // Write JSON files
            // 1. System Provider (loaded after built-in)
            string systemJson = @"{
                ""id"": ""provider-system"",
                ""label"": ""System Provider"",
                ""transportKind"": ""anthropic"",
                ""defaultModels"": {
                    ""small"": ""sys-small"",
                    ""large"": ""sys-large""
                }
            }";
            File.WriteAllText(Path.Combine(systemProvidersDir, "provider-system.json"), systemJson);

            // 2. User Provider (loaded after system)
            string userJson = @"{
                ""id"": ""provider-user"",
                ""label"": ""User Provider"",
                ""transportKind"": ""anthropic"",
                ""defaultModels"": {
                    ""small"": ""user-small"",
                    ""large"": ""user-large""
                }
            }";
            File.WriteAllText(Path.Combine(userProvidersDir, "provider-user.json"), userJson);

            // 3. Workspace Provider (loaded after user)
            string workspaceJson = @"{
                ""id"": ""provider-workspace"",
                ""label"": ""Workspace Provider"",
                ""transportKind"": ""anthropic"",
                ""defaultModels"": {
                    ""small"": ""ws-small"",
                    ""large"": ""ws-large""
                }
            }";
            File.WriteAllText(Path.Combine(workspaceProvidersDir, "provider-workspace.json"), workspaceJson);

            // 4. Overriding test: Write same ID "provider-system" in User directory and Workspace directory.
            // Workspace should win.
            string systemOverrideInWorkspaceJson = @"{
                ""id"": ""provider-system"",
                ""label"": ""System Provider Overridden by Workspace"",
                ""transportKind"": ""anthropic"",
                ""defaultModels"": {
                    ""small"": ""overridden-small"",
                    ""large"": ""overridden-large""
                }
            }";
            File.WriteAllText(Path.Combine(workspaceProvidersDir, "provider-system.json"), systemOverrideInWorkspaceJson);

            // Action
            var registry = ProviderRegistry.CreateWithDefaults(workspaceDir);

            // Assertions
            // Built-in should exist
            Assert.NotNull(registry.Get("gemini"));

            // System provider should be present and overridden by Workspace
            var sysProvider = registry.Get("provider-system");
            Assert.NotNull(sysProvider);
            Assert.Equal("System Provider Overridden by Workspace", sysProvider.Label);
            Assert.Equal("overridden-small", sysProvider.DefaultModels.Small);

            // User provider should be present
            var usrProvider = registry.Get("provider-user");
            Assert.NotNull(usrProvider);
            Assert.Equal("User Provider", usrProvider.Label);

            // Workspace provider should be present
            var wsProvider = registry.Get("provider-workspace");
            Assert.NotNull(wsProvider);
            Assert.Equal("Workspace Provider", wsProvider.Label);
        }

        [Fact]
        public void ApplyPrecedence_ShouldRespectHierarchy()
        {
            // Clear environment to begin with
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_PROVIDER", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_PROVIDER", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_MODEL", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_MODEL", null);

            // Case 1: Only Defaults (config is empty, env is empty, cli is empty)
            var config = new GlobalConfig();
            SettingsManager.ApplyPrecedence(config, null, null);

            Assert.Equal("gemini", AppState.ActiveProvider);
            Assert.Equal("gemini-3.1-flash-lite-preview", AppState.ActiveModel);
            Assert.False(AppState.IsProviderExplicitlySet);

            // Case 2: Config values specified
            config.ActiveProvider = "config-prov";
            config.ActiveModel = "config-mod";
            SettingsManager.ApplyPrecedence(config, null, null);

            Assert.Equal("config-prov", AppState.ActiveProvider);
            Assert.Equal("config-mod", AppState.ActiveModel);
            Assert.True(AppState.IsProviderExplicitlySet);

            // Case 3: Environment variables override Config
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_PROVIDER", "env-prov");
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_MODEL", "env-mod");
            SettingsManager.ApplyPrecedence(config, null, null);

            Assert.Equal("env-prov", AppState.ActiveProvider);
            Assert.Equal("env-mod", AppState.ActiveModel);
            Assert.True(AppState.IsProviderExplicitlySet);

            // Test fallback env var CLAUDE4NET_PROVIDER / CLAUDE4NET_MODEL
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_PROVIDER", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_ACTIVE_MODEL", null);
            Environment.SetEnvironmentVariable("CLAUDE4NET_PROVIDER", "env-prov-fallback");
            Environment.SetEnvironmentVariable("CLAUDE4NET_MODEL", "env-mod-fallback");
            SettingsManager.ApplyPrecedence(config, null, null);

            Assert.Equal("env-prov-fallback", AppState.ActiveProvider);
            Assert.Equal("env-mod-fallback", AppState.ActiveModel);
            Assert.True(AppState.IsProviderExplicitlySet);

            // Case 4: CLI options override Environment
            SettingsManager.ApplyPrecedence(config, "cli-prov", "cli-mod");

            Assert.Equal("cli-prov", AppState.ActiveProvider);
            Assert.Equal("cli-mod", AppState.ActiveModel);
            Assert.True(AppState.IsProviderExplicitlySet);
        }

        [Fact]
        public async Task SettingsManager_GetMergedSettings_ShouldMergeUserAndWorkspaceConfig()
        {
            var userDir = Path.Combine(_tempRoot, "user");
            Directory.CreateDirectory(userDir);
            var workspaceDir = Path.Combine(_tempRoot, "workspace");
            var workspaceDotDir = Path.Combine(workspaceDir, ".claude4net");
            Directory.CreateDirectory(workspaceDotDir);

            // Set up config paths
            var userConfigFilePath = Path.Combine(userDir, "config.json");
            SettingsManager.UserConfigPathResolver = () => userConfigFilePath;

            // Write user config
            string userConfigJson = @"{
                ""Theme"": ""light"",
                ""Verbose"": true,
                ""ActiveProvider"": ""user-provider"",
                ""ActiveModel"": ""user-model""
            }";
            File.WriteAllText(userConfigFilePath, userConfigJson);

            // Write workspace config
            string workspaceConfigJson = @"{
                ""Verbose"": false,
                ""ActiveProvider"": ""ws-provider""
            }";
            File.WriteAllText(Path.Combine(workspaceDotDir, "config.json"), workspaceConfigJson);

            // Action
            var merged = await SettingsManager.GetMergedSettingsAsync(workspaceDir);

            // Assertions:
            // Theme should be "light" (inherited from user config)
            Assert.Equal("light", merged.Theme);
            // Verbose should be false (workspace overrides user config)
            Assert.False(merged.Verbose);
            // ActiveProvider should be ws-provider (workspace overrides user config)
            Assert.Equal("ws-provider", merged.ActiveProvider);
            // ActiveModel should be user-model (inherited from user config since workspace config didn't define it)
            Assert.Equal("user-model", merged.ActiveModel);
        }
    }
}
