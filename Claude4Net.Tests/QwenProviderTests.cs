using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Moq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class QwenProviderTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<IToolRegistry> _mockToolRegistry;

        public QwenProviderTests()
        {
            var services = new ServiceCollection();

            _mockToolRegistry = new Mock<IToolRegistry>();
            _mockToolRegistry.Setup(t => t.GetTools()).Returns(new List<ITool>().AsReadOnly());
            services.AddSingleton(_mockToolRegistry.Object);

            services.AddHttpClient();

            // Factories
            services.AddSingleton<IProviderFactory, QwenProviderFactory>();
            services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();

            _serviceProvider = services.BuildServiceProvider();
        }

        // ──────────────────────────────────────────────
        // 상수 검증
        // ──────────────────────────────────────────────

        [Fact]
        public void QwenProvider_Constants_HaveExpectedValues()
        {
            Assert.Equal("https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1", QwenProvider.DefaultEndpoint);
            Assert.Equal("https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1", QwenProvider.TokenPlanEndpoint);
            Assert.Equal("https://dashscope.aliyuncs.com/compatible-mode/v1", QwenProvider.DashScopeEndpoint);
            Assert.Equal("https://dashscope-intl.aliyuncs.com/compatible-mode/v1", QwenProvider.InternationalEndpoint);
            Assert.Equal("qwen3.6-flash", QwenProvider.DefaultSmallModel);
            Assert.Equal("qwen3.8-max", QwenProvider.DefaultLargeModel);
            Assert.Equal("qwen3.8-max", QwenProvider.ModelQwen38Max);
            Assert.Equal("qwen3.7-plus", QwenProvider.ModelQwen37Plus);
            Assert.Equal("qwen3.7-max", QwenProvider.ModelQwen37Max);
            Assert.Equal("deepseek-v4-pro-0813", QwenProvider.ModelDeepSeekV4Pro0813);
            Assert.Equal("glm-5.2", QwenProvider.ModelGlm52);
            Assert.Equal(131_072, QwenProvider.DefaultContextWindowSize);
            Assert.Equal("text-embedding-v3", QwenProvider.DefaultEmbeddingModel);
        }

        // ──────────────────────────────────────────────
        // ILLMProvider 기본 프로퍼티
        // ──────────────────────────────────────────────

        [Fact]
        public void QwenProvider_Name_IsQwen()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.Equal("qwen", provider.Name);
        }

        [Fact]
        public void QwenProvider_ContextLimit_IsDefault()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.Equal(QwenProvider.DefaultContextWindowSize, provider.ContextLimit);
        }

        [Fact]
        public void QwenProvider_TokenCounter_IsNotNull()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);
            Assert.NotNull(provider.TokenCounter);
        }

        // ──────────────────────────────────────────────
        // 메시지 히스토리 관리
        // ──────────────────────────────────────────────

        [Fact]
        public void QwenProvider_AddMessage_GenericObject_AddsToHistory()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);
            var msg = new { role = "user", content = "Hello Qwen" };
            provider.AddMessage(msg);

            Assert.Single(provider.GetHistory());
        }

        [Fact]
        public void QwenProvider_SetHistory_ReplacesEntireHistory()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);
            provider.AddMessage(new { role = "user", content = "1" });
            provider.AddMessage(new { role = "assistant", content = "2" });

            provider.SetHistory(new List<object> { new { role = "user", content = "new" } });

            Assert.Single(provider.GetHistory());
        }

        [Fact]
        public void QwenProvider_AddMessage_ToolResult_ConvertsToToolRole()
        {
            var provider = new QwenProvider(new HttpClient(), _mockToolRegistry.Object);

            var toolResultJson = JsonSerializer.Deserialize<JsonElement>(
                """
                {
                    "role": "user",
                    "content": [
                        {
                            "type": "tool_result",
                            "tool_use_id": "call_123",
                            "content": "output text"
                        }
                    ]
                }
                """);

            provider.AddMessage(toolResultJson);

            var history = provider.GetHistory();
            Assert.Single(history);

            var json = JsonSerializer.Serialize(history[0]);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("tool", doc.RootElement.GetProperty("role").GetString());
            Assert.Equal("call_123", doc.RootElement.GetProperty("tool_call_id").GetString());
            Assert.Equal("output text", doc.RootElement.GetProperty("content").GetString());
        }

        // ──────────────────────────────────────────────
        // QwenProviderFactory 검증
        // ──────────────────────────────────────────────

        [Theory]
        [InlineData("qwen")]
        [InlineData("QWEN")]
        [InlineData("alibaba")]
        [InlineData("dashscope")]
        [InlineData("qwen-coder")]
        [InlineData("alibaba-coding")]
        public void QwenProviderFactory_CanCreate_WhenIdMatches(string providerId)
        {
            var factory = new QwenProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = providerId,
                Label = "Alibaba Qwen",
                TransportKind = "openai-compat",
                Endpoint = QwenProvider.DefaultEndpoint
            };

            Assert.True(factory.CanCreate(descriptor));
        }

        [Fact]
        public void QwenProviderFactory_Create_ReturnsQwenProvider()
        {
            var factory = new QwenProviderFactory();
            var descriptor = new ProviderDescriptor
            {
                Id = "qwen",
                Label = "Alibaba Qwen",
                TransportKind = "openai-compat",
                Endpoint = QwenProvider.DefaultEndpoint,
                DefaultModels = new ProviderDefaultModels
                {
                    Small = QwenProvider.DefaultSmallModel,
                    Large = QwenProvider.DefaultLargeModel
                }
            };

            var provider = factory.Create(descriptor, _serviceProvider);

            Assert.NotNull(provider);
            Assert.IsType<QwenProvider>(provider);
            Assert.Equal("qwen", provider.Name);
        }

        [Fact]
        public void ProviderRegistry_HasAlibabaQwenRegistered()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            var descriptor = registry.Get("qwen");

            Assert.NotNull(descriptor);
            Assert.Equal("qwen", descriptor!.Id);
            Assert.Equal("openai-compat", descriptor.TransportKind);
            Assert.Equal(QwenProvider.DefaultEndpoint, descriptor.Endpoint);
            Assert.Equal(QwenProvider.DefaultSmallModel, descriptor.DefaultModels.Small);
            Assert.Equal(QwenProvider.DefaultLargeModel, descriptor.DefaultModels.Large);
            Assert.Equal(QwenProvider.DefaultContextWindowSize, descriptor.ContextWindowSize);
            Assert.True(descriptor.Capabilities.Streaming);
            Assert.True(descriptor.Capabilities.ToolCalling);
            Assert.True(descriptor.Capabilities.Embeddings);
            Assert.True(descriptor.Capabilities.ThoughtSignature);
        }

        [Fact]
        public void ProviderRegistry_HasAlibabaRegistered()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            var descriptor = registry.Get("alibaba");

            Assert.NotNull(descriptor);
            Assert.Equal("alibaba", descriptor!.Id);
            Assert.Equal("openai-compat", descriptor.TransportKind);
            Assert.Equal(QwenProvider.DefaultEndpoint, descriptor.Endpoint);
            Assert.Equal(QwenProvider.DefaultSmallModel, descriptor.DefaultModels.Small);
            Assert.Equal(QwenProvider.DefaultLargeModel, descriptor.DefaultModels.Large);
        }
    }
}
