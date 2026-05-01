using Xunit;
using Claude4Net.SDK;
using System.IO;
using System;
using System.Threading;

namespace Claude4Net.Tests
{
    public class D06ResourceSkillsTests : IDisposable
    {
        private readonly string _testResourcesDir;

        public D06ResourceSkillsTests()
        {
            _testResourcesDir = Path.Combine(Path.GetTempPath(), "Claude4Net_TestResources_" + Guid.NewGuid());
            Directory.CreateDirectory(_testResourcesDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testResourcesDir))
                Directory.Delete(_testResourcesDir, true);
        }

        [Fact]
        public void SkillResourceLoader_ShouldLoadExistingFiles()
        {
            // Arrange
            string pluginDir = Path.Combine(_testResourcesDir, "TestPlugin");
            Directory.CreateDirectory(pluginDir);
            File.WriteAllText(Path.Combine(pluginDir, "checklist.md"), "Test Checklist");
            File.WriteAllText(Path.Combine(pluginDir, "examples.md"), "Test Examples");

            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest = loader.LoadForPlugin("TestPlugin");

            // Assert
            Assert.Equal("Test Checklist", manifest.Checklist?.Trim());
            Assert.Equal("Test Examples", manifest.Examples?.Trim());
            Assert.Null(manifest.ErrorPlaybook);
            Assert.False(manifest.IsEmpty);
        }

        [Fact]
        public void SkillResourceLoader_ShouldReturnEmptyForNonExistentPlugin()
        {
            // Arrange
            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest = loader.LoadForPlugin("NonExistent");

            // Assert
            Assert.True(manifest.IsEmpty);
        }

        [Fact]
        public void SkillResourceLoader_ShouldInvalidateCacheOnUpdate()
        {
            // Arrange
            string pluginDir = Path.Combine(_testResourcesDir, "CachePlugin");
            Directory.CreateDirectory(pluginDir);
            string filePath = Path.Combine(pluginDir, "checklist.md");
            File.WriteAllText(filePath, "Version 1");

            var loader = new SkillResourceLoader(_testResourcesDir);
            loader.ClearCache();

            // Act 1: Initial Load
            var manifest1 = loader.LoadForPlugin("CachePlugin");
            Assert.Equal("Version 1", manifest1.Checklist?.Trim());

            // Act 2: Update file (Wait a bit to ensure timestamp change if file system resolution is low)
            Thread.Sleep(100);
            File.WriteAllText(filePath, "Version 2");
            
            // Act 3: Load again
            var manifest2 = loader.LoadForPlugin("CachePlugin");

            // Assert
            Assert.Equal("Version 2", manifest2.Checklist?.Trim());
        }

        [Fact]
        public void SkillResourceLoader_ShouldHandleMissingFilesGracefully()
        {
             // Arrange
            string pluginDir = Path.Combine(_testResourcesDir, "PartialPlugin");
            Directory.CreateDirectory(pluginDir);
            File.WriteAllText(Path.Combine(pluginDir, "execution-protocol.md"), "Protocol only");

            var loader = new SkillResourceLoader(_testResourcesDir);

            // Act
            var manifest = loader.LoadForPlugin("PartialPlugin");

            // Assert
            Assert.Equal("Protocol only", manifest.ExecutionProtocol?.Trim());
            Assert.Null(manifest.Checklist);
            Assert.Null(manifest.ErrorPlaybook);
            Assert.Null(manifest.Examples);
        }
    }
}
