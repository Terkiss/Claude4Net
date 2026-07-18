using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Moq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class GlmProviderTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IToolRegistry> _mockToolRegistry;

        public GlmProviderTests()
        {
            var services = new ServiceCollection();

            _mockToolRegistry = new Mock<IToolRegistry>();
            _mockToolRegistry.Setup(t => t.GetTools()).Returns(new List<ITool>().AsReadOnly());
            services.AddSingleton(_mockToolRegistry.Object);

            services.AddHttpClient();

            // Factories
            services.AddSingleton<IProviderFactory, GlmProviderFactory>();
            services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();

            _serviceProvider = services.BuildServiceProvider();
        }

        // ──────────────────────────────────────────────
        // 상수 검증
        // ──────────────────────────────────────────────

        [Fact]
        public void GlmProvider_Constants_HaveExpectedValues()
        {
            Assert.Equal("https://open.bigmodel.cn/api/paas/v4", GlmProvider.DefaultEndpoint);
            Assert.Equal("glm-4-flash", GlmProvider.DefaultSmallModel);
            Assert.Equal("glm-4-plus", GlmProvider.DefaultLargeModel);
            Assert.Equal(128_000, GlmProvider.DefaultContextWindowSize);
            Assert.Equal("embedding-3", GlmProvider.DefaultEmbeddingModel);
        }

        // ──────────────────────────────────────────────
        // ILLMProvider 기본 프로퍼티
        // ──────────────────────────────────────────────

        [Fact]
        public void GlmProvider_Name_IsGlm()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.Equal("glm", provider.Name);
        }

        [Fact]
        public void GlmProvider_ContextLimit_IsDefault()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.Equal(GlmProvider.DefaultContextWindowSize, provider.ContextLimit);
        }

        [Fact]
        public void GlmProvider_TokenCounter_IsNotNull()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.NotNull(provider.TokenCounter);
        }

        // ──────────────────────────────────────────────
        // 메시지 히스토리 관리
        // ──────────────────────────────────────────────

        [Fact]
        public void GlmProvider_AddMessage_GenericObject_AddsToHistory()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            provider.AddMessage(new { role = "user", content = "hello" });

            Assert.Single(provider.GetHistory());
        }

        [Fact]
        public void GlmProvider_SetHistory_ReplacesEntireHistory()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            provider.AddMessage(new { role = "user", content = "old" });
            provider.SetHistory(new[] { new { role = "user", content = "new" } });

            Assert.Single(provider.GetHistory());
        }

        [Fact]
        public void GlmProvider_AddMessage_ToolResult_ConvertsToToolRole()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);

            // Anthropic 형식의 tool_result 메시지
            var toolResultMsg = new
            {
                role = "user",
                content = new[]
                {
                    new { type = "tool_result", tool_use_id = "call_123", content = "result text" }
                }
            };

            provider.AddMessage(toolResultMsg);
            var history = provider.GetHistory();

            Assert.Single(history);
            // 변환 후 role이 "tool"이어야 함
            var json = System.Text.Json.JsonSerializer.Serialize(history[0]);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal("tool", doc.RootElement.GetProperty("role").GetString());
            Assert.Equal("call_123", doc.RootElement.GetProperty("tool_call_id").GetString());
        }

        [Fact]
        public void GlmProvider_AddMessage_Null_DoesNotThrow()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            provider.AddMessage(null!);

            Assert.Empty(provider.GetHistory());
        }

        // ──────────────────────────────────────────────
        // Factory 매칭
        // ──────────────────────────────────────────────

        [Fact]
        public void GlmProviderFactory_CanCreate_WhenIdIsGlm()
        {
            var factory = new GlmProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "glm",
                Label = "Zhipu GLM",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels { Small = "s", Large = "l" }
            };

            Assert.True(factory.CanCreate(descriptor));
        }

        [Fact]
        public void GlmProviderFactory_CannotCreate_WhenIdIsNotGlm()
        {
            var factory = new GlmProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "ollama",
                Label = "Ollama",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels { Small = "s", Large = "l" }
            };

            Assert.False(factory.CanCreate(descriptor));
        }

        [Fact]
        public void GlmProviderFactory_CannotCreate_WhenDescriptorIsNull()
        {
            var factory = new GlmProviderFactory();
            Assert.False(factory.CanCreate(null!));
        }

        [Fact]
        public void GlmProviderFactory_Create_ReturnsGlmProvider()
        {
            var factory = new GlmProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "glm",
                Label = "Zhipu GLM",
                TransportKind = "openai-compat",
                Endpoint = GlmProvider.DefaultEndpoint,
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "glm-4-flash",
                    Large = "glm-4-plus"
                },
                Auth = new ProviderAuth { Mode = "none" }
            };

            var provider = factory.Create(descriptor, _serviceProvider);

            Assert.NotNull(provider);
            Assert.IsType<GlmProvider>(provider);
            Assert.Equal("glm", provider.Name);
        }

        // ──────────────────────────────────────────────
        // Registry 통합
        // ──────────────────────────────────────────────

        [Fact]
        public void ProviderRegistry_GlmDescriptor_IsRegisteredByDefault()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            var descriptor = registry.Get("glm");

            Assert.NotNull(descriptor);
            Assert.Equal("glm", descriptor!.Id);
            Assert.Equal("openai-compat", descriptor.TransportKind);
            Assert.Equal(GlmProvider.DefaultEndpoint, descriptor.Endpoint);
            Assert.Equal(GlmProvider.DefaultSmallModel, descriptor.DefaultModels.Small);
            Assert.Equal(GlmProvider.DefaultLargeModel, descriptor.DefaultModels.Large);
            Assert.True(descriptor.Capabilities.ToolCalling);
            Assert.True(descriptor.Capabilities.Streaming);
            Assert.True(descriptor.Capabilities.Vision);
            Assert.True(descriptor.Capabilities.Embeddings);
        }

        [Fact]
        public void ProviderRegistry_CreateProvider_Glm_UsesGlmProviderFactory()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            var provider = registry.CreateProvider("glm", _serviceProvider);

            Assert.NotNull(provider);
            Assert.IsType<GlmProvider>(provider);
        }

        // ──────────────────────────────────────────────
        // 엔드포인트 해석
        // ──────────────────────────────────────────────

        [Fact]
        public void GlmProvider_ImplementsILLMProvider()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.IsAssignableFrom<ILLMProvider>(provider);
        }

        [Fact]
        public void GlmProvider_ImplementsIEmbeddingProvider()
        {
            var provider = new GlmProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.IsAssignableFrom<IEmbeddingProvider>(provider);
        }
    }
}
