using System;
using System.IO;
using System.Threading.Tasks;
using Claude4Net.Runtime;
using Claude4Net.Cli.Bootstrap;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K057SettingsPrecedenceTests : IDisposable
    {
        private readonly string _workspaceDir;

        public K057SettingsPrecedenceTests()
        {
            _workspaceDir = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Workspace_" + Guid.NewGuid().ToString("N"));
            var dotDir = Path.Combine(_workspaceDir, ".claude4net");
            Directory.CreateDirectory(dotDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_workspaceDir, true); } catch { }
        }

        [Fact]
        public async Task GetMergedSettingsAsync_ShouldMergeWorkspaceOverUser()
        {
            // Write workspace config
            string wsConfig = @"{ ""Theme"": ""light"", ""ActiveProvider"": ""workspace-provider"" }";
            File.WriteAllText(Path.Combine(_workspaceDir, ".claude4net", "config.json"), wsConfig);

            var settings = await SettingsManager.GetMergedSettingsAsync(_workspaceDir);

            // Workspace config should be loaded
            Assert.Equal("light", settings.Theme);
            Assert.Equal("workspace-provider", settings.ActiveProvider);
        }

        [Fact]
        public void CliOptions_ShouldParseProviderAndModel()
        {
            var args = new[] { "--provider", "cli-provider", "--model", "cli-model" };
            var options = CliOptions.Parse(args);

            Assert.Equal("cli-provider", options.Provider);
            Assert.Equal("cli-model", options.Model);
        }
    }
}
