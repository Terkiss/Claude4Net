using System;
using System.Linq;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K031 Routing V2 Tests: SmartRouter의 디스크립터 기반 라우팅 검증
    /// </summary>
    [Collection("AppState")]
    public class K031RoutingV2Tests
    {
        /// <summary>
        /// SmartRouter가 디스크립터의 기본 모델을 사용하는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_UsesDescriptorDefaultModel()
        {
            // AppState 초기화 (명시적 설정 없음)
            AppState.IsProviderExplicitlySet = false;

            var router = new SmartRouter();

            // LargeModel 의도로 라우팅
            var decision = router.Route("please refactor this complex code base", RoutingIntent.LargeModel);

            // 선택된 모델이 디스크립터에 정의된 모델인지 확인
            var registry = router.Registry;
            var descriptor = registry.Get(decision.SelectedProvider);
            Assert.NotNull(descriptor);

            // 모델이 디스크립터의 Large 또는 Small 모델과 일치해야 함
            Assert.True(
                decision.SelectedModel == descriptor.DefaultModels.Large ||
                decision.SelectedModel == descriptor.DefaultModels.Small,
                $"Selected model '{decision.SelectedModel}' should match descriptor defaults " +
                $"(Small: {descriptor.DefaultModels.Small}, Large: {descriptor.DefaultModels.Large})");
        }

        /// <summary>
        /// 사용자가 명시적으로 프로바이더를 지정한 경우 존중되는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_RespectsForcedProvider()
        {
            AppState.ActiveProvider = "claude";
            AppState.ActiveModel = "claude-3-5-sonnet-custom";
            AppState.IsProviderExplicitlySet = true;

            try
            {
                var router = new SmartRouter();
                var decision = router.Route("test prompt", RoutingIntent.Auto);

                Assert.Equal("claude", decision.SelectedProvider);
                Assert.Equal("claude-3-5-sonnet-custom", decision.SelectedModel);
            }
            finally
            {
                AppState.IsProviderExplicitlySet = false;
                AppState.ActiveProvider = "gemini";
                AppState.ActiveModel = "gemini-2.0-flash";
            }
        }

        /// <summary>
        /// SmartRouter가 ProviderRegistry를 노출하는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_ExposesRegistry()
        {
            var router = new SmartRouter();
            Assert.NotNull(router.Registry);
            Assert.True(router.Registry.Count >= 4);
        }

        /// <summary>
        /// 커스텀 레지스트리로 SmartRouter를 초기화할 수 있는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_CustomRegistry()
        {
            AppState.IsProviderExplicitlySet = false;

            var registry = new ProviderRegistry();
            registry.Register(new ProviderDescriptor
            {
                Id = "test-provider",
                Label = "Test Provider",
                TransportKind = "test",
                DefaultModels = new ProviderDefaultModels { Small = "test-small", Large = "test-large" },
                Capabilities = new ProviderCapabilities { ToolCalling = true, Local = true },
                Auth = new ProviderAuth { Mode = "none" },
                CostScore = 0.0,
                SupportedCategories = new[] { RoutingCategory.QuickFix }
            });

            var router = new SmartRouter(registry);
            Assert.NotNull(router.Registry.Get("test-provider"));

            // 유일한 프로바이더이므로 선택되어야 함
            var decision = router.Route("test", RoutingIntent.Auto);
            Assert.Equal("test-provider", decision.SelectedProvider);
        }

        /// <summary>
        /// LocalOnly 의도에서 로컬 프로바이더가 우선되는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_LocalOnlyPrefersLocalProvider()
        {
            AppState.IsProviderExplicitlySet = false;

            var router = new SmartRouter();
            var decision = router.Route("test local only", RoutingIntent.LocalOnly);

            // 로컬 프로바이더 (ollama 또는 gemini-cli) 선택 확인
            Assert.True(
                router.Registry.IsLocal(decision.SelectedProvider),
                $"Expected local provider but got '{decision.SelectedProvider}'");
        }

        /// <summary>
        /// LocalPrivate 카테고리에 리모트 프로바이더가 할당되지 않는지 검증
        /// </summary>
        [Fact]
        public void SmartRouter_LocalPrivateRejectsRemoteProvider()
        {
            var registry = ProviderRegistry.CreateWithDefaults();
            var localPrivateProviders = registry.GetByCategory(RoutingCategory.LocalPrivate);

            // LocalPrivate 카테고리의 모든 프로바이더는 Local이어야 함
            foreach (var provider in localPrivateProviders)
            {
                Assert.True(provider.Capabilities.Local,
                    $"Provider '{provider.Id}' in LocalPrivate category must be local");
            }
        }
    }
}
