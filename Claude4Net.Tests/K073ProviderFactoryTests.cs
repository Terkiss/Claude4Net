using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Moq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class K073ProviderFactoryTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IToolRegistry> _mockToolRegistry;

        public K073ProviderFactoryTests()
        {
            var services = new ServiceCollection();

            _mockToolRegistry = new Mock<IToolRegistry>();
            services.AddSingleton(_mockToolRegistry.Object);

            // Register real clients/dependencies
            services.AddSingleton<AnthropicClient>(sp => new AnthropicClient(new HttpClient()));

            // Register real provider instances
            services.AddSingleton<ClaudeService>();
            services.AddSingleton<GeminiProvider>(sp => new GeminiProvider(new HttpClient(), _mockToolRegistry.Object));
            services.AddSingleton<OllamaProvider>(sp => new OllamaProvider(new HttpClient(), _mockToolRegistry.Object));
            services.AddSingleton<GeminiCliProvider>();

            // Factories
            services.AddSingleton<IProviderFactory, AnthropicProviderFactory>();
            services.AddSingleton<IProviderFactory, GeminiProviderFactory>();
            services.AddSingleton<IProviderFactory, OllamaProviderFactory>();
            services.AddSingleton<IProviderFactory, GeminiCliProviderFactory>();
            services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();

            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public void HappyPath_AnthropicProviderFactory_ShouldCreateClaudeService()
        {
            var factory = new AnthropicProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "claude",
                Label = "Claude Provider",
                TransportKind = "anthropic",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<ClaudeService>(provider);
        }

        [Fact]
        public void HappyPath_GeminiProviderFactory_ShouldCreateGeminiProvider()
        {
            var factory = new GeminiProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "gemini",
                Label = "Gemini Provider",
                TransportKind = "gemini-native",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<GeminiProvider>(provider);
        }

        [Fact]
        public void HappyPath_OllamaProviderFactory_ShouldCreateOllamaProvider()
        {
            var factory = new OllamaProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "ollama",
                Label = "Ollama Provider",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OllamaProvider>(provider);
        }

        [Fact]
        public void HappyPath_GeminiCliProviderFactory_ShouldCreateGeminiCliProvider()
        {
            var factory = new GeminiCliProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "gemini-cli",
                Label = "Gemini CLI Provider",
                TransportKind = "cli",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<GeminiCliProvider>(provider);
        }

        [Fact]
        public void HappyPath_OpenAiCompatProviderFactory_ShouldCreateOpenAiCompatProvider()
        {
            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai",
                Label = "Custom OpenAI Compatible Provider",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth
                {
                    Mode = "none"
                }
            };

            Assert.True(factory.CanCreate(descriptor));
            var provider = factory.Create(descriptor, _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OpenAiCompatProvider>(provider);
            Assert.Equal("custom-openai", provider.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("invalid-url")]
        [InlineData("ftp://api.com")]
        public void OpenAiCompatProviderFactory_ShouldThrow_OnMalformedOrEmptyEndpoint(string endpoint)
        {
            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai",
                Label = "Custom OpenAI",
                TransportKind = "openai-compat",
                Endpoint = endpoint,
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth { Mode = "none" }
            };

            Assert.Throws<ArgumentException>(() => factory.Create(descriptor, _serviceProvider));
        }

        [Fact]
        public void OpenAiCompatProviderFactory_ShouldThrow_WhenApiKeyMissing()
        {
            string envVarName = "TEST_K073_MISSING_API_KEY";
            Environment.SetEnvironmentVariable(envVarName, null);

            var factory = new OpenAiCompatProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-openai-missing",
                Label = "Custom OpenAI Missing Key",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { envVarName }
                }
            };

            Assert.Throws<InvalidOperationException>(() => factory.Create(descriptor, _serviceProvider));
        }

        [Fact]
        public void OpenAiCompatProviderFactory_ShouldSucceed_WhenApiKeyExists()
        {
            string envVarName = "TEST_K073_EXISTS_API_KEY";
            Environment.SetEnvironmentVariable(envVarName, "test-key-value");

            try
            {
                var factory = new OpenAiCompatProviderFactory();
                var descriptor = new ProviderDescriptor
                {
                    Id = "custom-openai-exists",
                    Label = "Custom OpenAI Exists Key",
                    TransportKind = "openai-compat",
                    Endpoint = "https://api.openai.com/v1",
                    DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                    Auth = new ProviderAuth
                    {
                        Mode = "api-key",
                        EnvVars = new[] { envVarName }
                    }
                };

                var provider = factory.Create(descriptor, _serviceProvider);
                Assert.NotNull(provider);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVarName, null);
            }
        }

        [Fact]
        public void ProviderRegistry_CreateProvider_ShouldUseFactoryFirst()
        {
            var registry = new ProviderRegistry();
            var descriptor = new ProviderDescriptor
            {
                Id = "custom-factory-prov",
                Label = "Custom Factory Provider",
                TransportKind = "openai-compat",
                Endpoint = "https://api.openai.com/v1",
                DefaultModels = new ProviderDefaultModels { Small = "small-model", Large = "large-model" },
                Auth = new ProviderAuth { Mode = "none" }
            };
            registry.Register(descriptor);

            var provider = registry.CreateProvider("custom-factory-prov", _serviceProvider);
            Assert.NotNull(provider);
            Assert.IsType<OpenAiCompatProvider>(provider);
        }

        [Fact]
        public void ProviderRegistry_CreateProvider_ShouldFallbackSafely_WhenNoDescriptorOrFactory()
        {
            var registry = new ProviderRegistry();
            var provider = registry.CreateProvider("non-existent", _serviceProvider);

            Assert.NotNull(provider);
            Assert.IsType<ClaudeService>(provider);
        }
    }
}
