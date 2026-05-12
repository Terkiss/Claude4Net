using System;
using System.Linq;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K031 Provider Descriptor Tests: 프로바이더 디스크립터 레지스트리 기능 검증
    /// </summary>
    public class K031ProviderDescriptorTests
    {
        /// <summary>
        /// 기본 프로바이더 디스크립터가 정상적으로 로드되는지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_LoadsDescriptors()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            Assert.True(registry.Count >= 4, "최소 4개 프로바이더(claude, gemini, ollama, gemini-cli)가 등록되어야 함");

            var claude = registry.Get("claude");
            Assert.NotNull(claude);
            Assert.Equal("Anthropic Claude", claude.Label);
            Assert.Equal("anthropic", claude.TransportKind);

            var gemini = registry.Get("gemini");
            Assert.NotNull(gemini);
            Assert.Equal("Google Gemini", gemini.Label);
            Assert.True(gemini.Capabilities.ThoughtSignature);

            var ollama = registry.Get("ollama");
            Assert.NotNull(ollama);
            Assert.True(ollama.Capabilities.Local);

            var geminiCli = registry.Get("gemini-cli");
            Assert.NotNull(geminiCli);
            Assert.Equal("oauth", geminiCli.Auth.Mode);
        }

        /// <summary>
        /// 유효하지 않은 디스크립터를 거부하는지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_RejectsInvalidDescriptor()
        {
            var registry = new ProviderRegistry();

            // ID가 비어있는 경우
            Assert.Throws<ArgumentException>(() =>
                registry.Register(new ProviderDescriptor { Id = "", Label = "Test", TransportKind = "test" }));

            // Label이 비어있는 경우
            Assert.Throws<ArgumentException>(() =>
                registry.Register(new ProviderDescriptor { Id = "test", Label = "", TransportKind = "test" }));

            // TransportKind가 비어있는 경우
            Assert.Throws<ArgumentException>(() =>
                registry.Register(new ProviderDescriptor { Id = "test", Label = "Test", TransportKind = "" }));

            // null 디스크립터
            Assert.Throws<ArgumentNullException>(() =>
                registry.Register(null!));
        }

        /// <summary>
        /// 기능 확인 메서드가 정확히 동작하는지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_CapabilityCheck()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            // Gemini는 Vision 지원
            Assert.True(registry.HasCapability("gemini", c => c.Vision));
            // Ollama는 Vision 미지원
            Assert.False(registry.HasCapability("ollama", c => c.Vision));
            // Claude는 임베딩 미지원
            Assert.False(registry.HasCapability("claude", c => c.Embeddings));
            // Gemini는 임베딩 지원
            Assert.True(registry.HasCapability("gemini", c => c.Embeddings));
        }

        /// <summary>
        /// 로컬 프로바이더 판별이 정확한지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_IsLocalCheck()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            Assert.True(registry.IsLocal("ollama"));
            Assert.True(registry.IsLocal("gemini-cli"));
            Assert.False(registry.IsLocal("claude"));
            Assert.False(registry.IsLocal("gemini"));
            Assert.False(registry.IsLocal("nonexistent"));
        }

        /// <summary>
        /// 기본 모델 조회가 정확한지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_GetDefaultModel()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            // Small 모델
            Assert.Equal("gemini-2.0-flash", registry.GetDefaultModel("gemini", preferLarge: false));
            Assert.Equal("claude-3-5-haiku-20241022", registry.GetDefaultModel("claude", preferLarge: false));

            // Large 모델
            Assert.Equal("gemini-1.5-pro", registry.GetDefaultModel("gemini", preferLarge: true));
            Assert.Equal("claude-3-5-sonnet-20241022", registry.GetDefaultModel("claude", preferLarge: true));

            // 존재하지 않는 프로바이더
            Assert.Null(registry.GetDefaultModel("nonexistent"));
        }

        /// <summary>
        /// 카테고리별 프로바이더 조회가 정확한지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_GetByCategory()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            var localPrivate = registry.GetByCategory(RoutingCategory.LocalPrivate);
            Assert.True(localPrivate.All(d => d.Capabilities.Local));

            var deepCode = registry.GetByCategory(RoutingCategory.DeepCode);
            Assert.True(deepCode.Count >= 2);
        }

        /// <summary>
        /// 대소문자 무관하게 프로바이더를 조회할 수 있는지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_CaseInsensitiveLookup()
        {
            var registry = ProviderRegistry.CreateWithDefaults();

            Assert.NotNull(registry.Get("Claude"));
            Assert.NotNull(registry.Get("GEMINI"));
            Assert.NotNull(registry.Get("Ollama"));
        }

        /// <summary>
        /// 커스텀 프로바이더를 등록할 수 있는지 검증
        /// </summary>
        [Fact]
        public void ProviderRegistry_CustomProviderRegistration()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            int initialCount = registry.Count;

            registry.Register(new ProviderDescriptor
            {
                Id = "custom-openai",
                Label = "Custom OpenAI",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels { Small = "gpt-3.5-turbo", Large = "gpt-4" },
                Capabilities = new ProviderCapabilities { ToolCalling = true, Streaming = true },
                Auth = new ProviderAuth { Mode = "api-key", EnvVars = new[] { "OPENAI_API_KEY" } },
                CostScore = 0.7
            });

            Assert.Equal(initialCount + 1, registry.Count);
            Assert.NotNull(registry.Get("custom-openai"));
        }
    }
}
