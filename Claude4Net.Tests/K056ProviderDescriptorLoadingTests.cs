using System;
using System.IO;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K056ProviderDescriptorLoadingTests : IDisposable
    {
        private readonly string _testDir;

        public K056ProviderDescriptorLoadingTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Providers_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }

        [Fact]
        public void LoadFromDirectory_ShouldLoadValidJsonAndOverrideBuiltin()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            // Create a custom descriptor json
            string json = @"
            {
                ""id"": ""ollama"",
                ""label"": ""Ollama Custom"",
                ""transportKind"": ""openai-compat"",
                ""endpoint"": ""http://localhost:11434"",
                ""defaultModels"": { ""small"": ""qwen2.5"", ""large"": ""qwen2.5"" }
            }";

            File.WriteAllText(Path.Combine(_testDir, "custom-ollama.json"), json);

            registry.LoadFromDirectory(_testDir);

            var ollama = registry.Get("ollama");
            Assert.NotNull(ollama);
            Assert.Equal("Ollama Custom", ollama.Label);
            Assert.Equal("qwen2.5", ollama.DefaultModels.Small);
        }

        [Fact]
        public void LoadFromDirectory_ShouldIgnoreInvalidJson()
        {
            var registry = new ProviderRegistry();
            File.WriteAllText(Path.Combine(_testDir, "invalid.json"), "{ invalid_json ]");

            var ex = Assert.Throws<InvalidOperationException>(() => registry.LoadFromDirectory(_testDir));
            Assert.Contains("invalid.json", ex.Message);
        }
    }
}
