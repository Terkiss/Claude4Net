using System;
using System.IO;
using System.Threading;
using Xunit;
using Claude4Net.SDK;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D06ResourceTests : IDisposable
    {
        private readonly string _testResourcesDir;

        public D06ResourceTests()
        {
            _testResourcesDir = Path.Combine(Path.GetTempPath(), "Claude4Net_TestResources_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testResourcesDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testResourcesDir))
            {
                Directory.Delete(_testResourcesDir, true);
            }
        }

        [Fact]
        public void LoadForPlugin_DiscoversAndLoadsResources()
        {
            // Arrange
            string pluginName = "test_plugin";
            string pluginDir = Path.Combine(_testResourcesDir, pluginName);
            Directory.CreateDirectory(pluginDir);
            File.WriteAllText(Path.Combine(pluginDir, "checklist.md"), "Checklist content");
            File.WriteAllText(Path.Combine(pluginDir, "examples.md"), "Examples content");

            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest = loader.LoadForPlugin(pluginName);

            // Assert
            Assert.Equal(pluginName, manifest.PluginName);
            Assert.Equal("Checklist content", manifest.Checklist);
            Assert.Equal("Examples content", manifest.Examples);
            Assert.Null(manifest.ErrorPlaybook);
            Assert.Null(manifest.ExecutionProtocol);
        }

        [Fact]
        public void LoadForPlugin_GracefulFallback_WhenDirectoryMissing()
        {
            // Arrange
            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest = loader.LoadForPlugin("non_existent_plugin");

            // Assert
            Assert.True(manifest.IsEmpty);
        }

        [Fact]
        public void LoadForPlugin_Caching_ReturnsCachedInstance()
        {
            // Arrange
            string pluginName = "cached_plugin";
            string pluginDir = Path.Combine(_testResourcesDir, pluginName);
            Directory.CreateDirectory(pluginDir);
            File.WriteAllText(Path.Combine(pluginDir, "checklist.md"), "Initial content");

            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest1 = loader.LoadForPlugin(pluginName);
            var manifest2 = loader.LoadForPlugin(pluginName);

            // Assert
            Assert.Same(manifest1, manifest2);
        }

        [Fact]
        public void LoadForPlugin_CacheInvalidation_WhenFileModified()
        {
            // Arrange
            string pluginName = "stale_plugin";
            string pluginDir = Path.Combine(_testResourcesDir, pluginName);
            Directory.CreateDirectory(pluginDir);
            string filePath = Path.Combine(pluginDir, "checklist.md");
            File.WriteAllText(filePath, "Initial content");

            var loader = new SkillResourceLoader(_testResourcesDir);
            var manifest1 = loader.LoadForPlugin(pluginName);

            // Act
            // Wait a bit to ensure timestamp difference
            Thread.Sleep(100);
            File.WriteAllText(filePath, "Updated content");
            
            var manifest2 = loader.LoadForPlugin(pluginName);

            // Assert
            Assert.NotSame(manifest1, manifest2);
            Assert.Equal("Updated content", manifest2.Checklist);
        }

        [Fact]
        public void SystemPromptBuilder_IncludesResources()
        {
            // Arrange
            string originalBaseDir = AppState.SystemBaseDir;
            try
            {
                // Create .resources in our test dir
                string weatherDir = Path.Combine(_testResourcesDir, ".resources", "weather_search");
                Directory.CreateDirectory(weatherDir);
                File.WriteAllText(Path.Combine(weatherDir, "checklist.md"), "도시 이름이 영문인지 확인한다");
                
                AppState.SystemBaseDir = _testResourcesDir;
                var builder = new SystemPromptBuilder();

                // Act
                string prompt = builder.Build("gemini");

                // Assert
                Assert.Contains("## 🛠️ Plugin-Specific Execution Resources", prompt);
                Assert.Contains("### [RESOURCE: weather_search]", prompt);
                Assert.Contains("#### ✅ Checklist", prompt);
                Assert.Contains("도시 이름이 영문인지 확인한다", prompt);
            }
            finally
            {
                AppState.SystemBaseDir = originalBaseDir;
            }
        }
    }
}
