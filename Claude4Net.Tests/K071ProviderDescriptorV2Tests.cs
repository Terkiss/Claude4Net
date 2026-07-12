using System;
using System.IO;
using System.Linq;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K071ProviderDescriptorV2Tests : IDisposable
    {
        private readonly string _testDir;

        public K071ProviderDescriptorV2Tests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "Claude4Net_Test_Providers_K071_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }

        [Fact]
        public void ValidDescriptor_WithEndpointHeadersMetadata_LoadsSuccessfully()
        {
            var registry = new ProviderRegistry();
            string json = @"
            {
                ""id"": ""custom-v2"",
                ""label"": ""Custom V2 Provider"",
                ""transportKind"": ""openai-compat"",
                ""endpoint"": ""https://api.custom-v2.com/v1"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" },
                ""headers"": {
                    ""X-Custom-Header"": ""Value1"",
                    ""Authorization"": ""Bearer local""
                },
                ""metadata"": {
                    ""tier"": ""premium"",
                    ""timeout_seconds"": 30
                }
            }";

            File.WriteAllText(Path.Combine(_testDir, "valid-v2.json"), json);
            registry.LoadFromDirectory(_testDir);

            var desc = registry.Get("custom-v2");
            Assert.NotNull(desc);
            Assert.Equal("custom-v2", desc.Id);
            Assert.Equal("Custom V2 Provider", desc.Label);
            Assert.Equal("openai-compat", desc.TransportKind);
            Assert.Equal("https://api.custom-v2.com/v1", desc.Endpoint);
            Assert.Equal("model-small", desc.DefaultModels.Small);

            Assert.NotNull(desc.Headers);
            Assert.Equal("Value1", desc.Headers["X-Custom-Header"]);
            Assert.Equal("Bearer local", desc.Headers["Authorization"]);

            Assert.NotNull(desc.Metadata);
            Assert.Equal("premium", desc.Metadata["tier"]?.ToString());
            Assert.Equal("30", desc.Metadata["timeout_seconds"]?.ToString());
        }

        [Theory]
        [InlineData("not-a-uri")]
        [InlineData("ftp://api.com")]
        [InlineData("http//missing-colon")]
        public void DescriptorEndpoint_InvalidUri_FailsClosed(string invalidUri)
        {
            var registry = new ProviderRegistry();
            string json = $@"
            {{
                ""id"": ""invalid-uri-provider"",
                ""label"": ""Invalid Uri Provider"",
                ""transportKind"": ""openai-compat"",
                ""endpoint"": ""{invalidUri}"",
                ""defaultModels"": {{ ""small"": ""model-small"", ""large"": ""model-large"" }}
            }}";

            File.WriteAllText(Path.Combine(_testDir, "invalid-uri.json"), json);

            var ex = Assert.Throws<InvalidOperationException>(() => registry.LoadFromDirectory(_testDir));
            Assert.Contains("invalid-uri.json", ex.Message);
        }

        [Fact]
        public void DescriptorValidationError_IncludesFileOrProviderId()
        {
            var registry = new ProviderRegistry();
            // Missing label
            string json = @"
            {
                ""id"": ""missing-label-provider"",
                ""transportKind"": ""openai-compat"",
                ""endpoint"": ""https://api.com"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" }
            }";

            string filePath = Path.Combine(_testDir, "missing-label.json");
            File.WriteAllText(filePath, json);

            var ex = Assert.Throws<InvalidOperationException>(() => registry.LoadFromDirectory(_testDir));
            Assert.Contains("missing-label.json", ex.Message);
            Assert.Contains("missing-label-provider", ex.Message);

            // Register without file path directly (Register method)
            var invalidDesc = new ProviderDescriptor
            {
                Id = "direct-invalid",
                Label = "", // Empty label
                TransportKind = "openai-compat"
            };

            var exDirect = Assert.Throws<ArgumentException>(() => registry.Register(invalidDesc));
            Assert.Contains("direct-invalid", exDirect.Message);
        }

        [Fact]
        public void RoutingCategories_AreCaseInsensitive()
        {
            var registry = new ProviderRegistry();
            string json = @"
            {
                ""id"": ""case-insensitive-categories"",
                ""label"": ""Case Insensitive"",
                ""transportKind"": ""gemini-native"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" },
                ""supportedCategories"": [ ""quickfix"", ""DEEPCODE"", ""Planner"" ]
            }";

            File.WriteAllText(Path.Combine(_testDir, "case-insensitive.json"), json);
            registry.LoadFromDirectory(_testDir);

            var desc = registry.Get("case-insensitive-categories");
            Assert.NotNull(desc);
            Assert.Contains(RoutingCategory.QuickFix, desc.SupportedCategories);
            Assert.Contains(RoutingCategory.DeepCode, desc.SupportedCategories);
            Assert.Contains(RoutingCategory.Planner, desc.SupportedCategories);
        }

        [Fact]
        public void UnknownRoutingCategory_FailsClosed()
        {
            var registry = new ProviderRegistry();
            string json = @"
            {
                ""id"": ""unknown-category-provider"",
                ""label"": ""Unknown Category"",
                ""transportKind"": ""gemini-native"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" },
                ""supportedCategories"": [ ""quickfix"", ""UnknownCategory"" ]
            }";

            File.WriteAllText(Path.Combine(_testDir, "unknown-category.json"), json);

            var ex = Assert.Throws<InvalidOperationException>(() => registry.LoadFromDirectory(_testDir));
            Assert.Contains("unknown-category.json", ex.Message);
            Assert.Contains("UnknownCategory", ex.Message);
        }

        [Fact]
        public void Headers_DefaultToEmpty_WhenOmitted()
        {
            var registry = new ProviderRegistry();
            string json = @"
            {
                ""id"": ""omitted-headers"",
                ""label"": ""Omitted Headers"",
                ""transportKind"": ""gemini-native"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" }
            }";

            File.WriteAllText(Path.Combine(_testDir, "omitted-headers.json"), json);
            registry.LoadFromDirectory(_testDir);

            var desc = registry.Get("omitted-headers");
            Assert.NotNull(desc);
            Assert.NotNull(desc.Headers);
            Assert.Empty(desc.Headers);
        }

        [Fact]
        public void Metadata_DefaultsToEmpty_WhenOmitted()
        {
            var registry = new ProviderRegistry();
            string json = @"
            {
                ""id"": ""omitted-metadata"",
                ""label"": ""Omitted Metadata"",
                ""transportKind"": ""gemini-native"",
                ""defaultModels"": { ""small"": ""model-small"", ""large"": ""model-large"" }
            }";

            File.WriteAllText(Path.Combine(_testDir, "omitted-metadata.json"), json);
            registry.LoadFromDirectory(_testDir);

            var desc = registry.Get("omitted-metadata");
            Assert.NotNull(desc);
            Assert.NotNull(desc.Metadata);
            Assert.Empty(desc.Metadata);
        }

        [Fact]
        public void ExistingBuiltInDescriptors_RemainLoadable()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            Assert.True(registry.Count >= 4);

            var claude = registry.Get("claude");
            Assert.NotNull(claude);
            Assert.Equal("Anthropic Claude", claude.Label);
            Assert.Equal("anthropic", claude.TransportKind);

            var gemini = registry.Get("gemini");
            Assert.NotNull(gemini);
            Assert.Equal("Google Gemini", gemini.Label);
            Assert.Equal("gemini-native", gemini.TransportKind);

            var ollama = registry.Get("ollama");
            Assert.NotNull(ollama);
            Assert.Equal("Ollama (Local)", ollama.Label);
            Assert.Equal("openai-compat", ollama.TransportKind);
            Assert.Equal("http://localhost:11434", ollama.Endpoint);

            var geminiCli = registry.Get("gemini-cli");
            Assert.NotNull(geminiCli);
            Assert.Equal("Gemini CLI (파기 - 오래된 버전, antigravity-cli 권장)", geminiCli.Label);
            Assert.Equal("cli", geminiCli.TransportKind);
        }
    }
}
