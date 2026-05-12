using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 프로바이더 디스크립터를 관리하는 레지스트리입니다.
    /// 기존 SmartRouter, Doctor, Dashboard에서 하드코딩되던 프로바이더 메타데이터를
    /// 중앙에서 선언적으로 관리합니다.
    /// </summary>
    public class ProviderRegistry
    {
        private readonly ConcurrentDictionary<string, ProviderDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 등록된 모든 프로바이더 디스크립터를 반환합니다.
        /// </summary>
        public IReadOnlyCollection<ProviderDescriptor> All => _descriptors.Values.ToList().AsReadOnly();

        /// <summary>
        /// 기본 내장 프로바이더 디스크립터를 로드하여 초기화합니다.
        /// </summary>
        public static ProviderRegistry CreateWithDefaults()
        {
            var registry = new ProviderRegistry();

            registry.Register(new ProviderDescriptor
            {
                Id = "claude",
                Label = "Anthropic Claude",
                TransportKind = "anthropic",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "claude-3-5-haiku-20241022",
                    Large = "claude-3-5-sonnet-20241022"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = true,
                    ThoughtSignature = false,
                    Streaming = true,
                    Embeddings = false,
                    Local = false
                },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { "ANTHROPIC_API_KEY", "CLAUDE_API_KEY" }
                },
                CostScore = 0.8,
                SupportedCategories = new[]
                {
                    RoutingCategory.DeepCode,
                    RoutingCategory.Planner,
                    RoutingCategory.Verifier
                },
                ContextWindowSize = 200_000
            });

            registry.Register(new ProviderDescriptor
            {
                Id = "gemini",
                Label = "Google Gemini",
                TransportKind = "gemini-native",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "gemini-2.0-flash",
                    Large = "gemini-1.5-pro"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = true,
                    ThoughtSignature = true,
                    Streaming = true,
                    Embeddings = true,
                    Local = false
                },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" }
                },
                CostScore = 0.4,
                SupportedCategories = new[]
                {
                    RoutingCategory.QuickFix,
                    RoutingCategory.DeepCode,
                    RoutingCategory.Planner,
                    RoutingCategory.VisualEngineering,
                    RoutingCategory.CheapUtility
                },
                ContextWindowSize = 1_000_000
            });

            registry.Register(new ProviderDescriptor
            {
                Id = "ollama",
                Label = "Ollama (Local)",
                TransportKind = "openai-compat",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "llama3",
                    Large = "llama3"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = false,
                    ThoughtSignature = false,
                    Streaming = true,
                    Embeddings = false,
                    Local = true
                },
                Auth = new ProviderAuth
                {
                    Mode = "none",
                    EnvVars = Array.Empty<string>()
                },
                CostScore = 0.1,
                SupportedCategories = new[]
                {
                    RoutingCategory.QuickFix,
                    RoutingCategory.LocalPrivate,
                    RoutingCategory.CheapUtility
                },
                ContextWindowSize = 8_000
            });

            registry.Register(new ProviderDescriptor
            {
                Id = "gemini-cli",
                Label = "Gemini CLI (Local OAuth)",
                TransportKind = "cli",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "gemini-2.0-flash",
                    Large = "gemini-3.1-pro"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = false,
                    ThoughtSignature = true,
                    Streaming = true,
                    Embeddings = false,
                    Local = true
                },
                Auth = new ProviderAuth
                {
                    Mode = "oauth",
                    EnvVars = Array.Empty<string>()
                },
                CostScore = 0.0,
                SupportedCategories = new[]
                {
                    RoutingCategory.QuickFix,
                    RoutingCategory.DeepCode,
                    RoutingCategory.Planner,
                    RoutingCategory.LocalPrivate,
                    RoutingCategory.CheapUtility
                },
                ContextWindowSize = 1_000_000
            });

            return registry;
        }

        /// <summary>
        /// 프로바이더 디스크립터를 등록합니다.
        /// ID가 비어있거나 유효하지 않으면 예외를 발생시킵니다.
        /// </summary>
        public void Register(ProviderDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException("Provider descriptor ID cannot be empty.", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Label))
                throw new ArgumentException("Provider descriptor label cannot be empty.", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.TransportKind))
                throw new ArgumentException("Provider descriptor transport kind cannot be empty.", nameof(descriptor));

            _descriptors[descriptor.Id] = descriptor;
        }

        /// <summary>
        /// 프로바이더 ID로 디스크립터를 조회합니다.
        /// </summary>
        public ProviderDescriptor? Get(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            _descriptors.TryGetValue(providerId, out var descriptor);
            return descriptor;
        }

        /// <summary>
        /// 프로바이더가 특정 기능을 지원하는지 확인합니다.
        /// </summary>
        public bool HasCapability(string providerId, Func<ProviderCapabilities, bool> check)
        {
            var descriptor = Get(providerId);
            return descriptor != null && check(descriptor.Capabilities);
        }

        /// <summary>
        /// 프로바이더 ID로 기본 모델을 가져옵니다.
        /// </summary>
        /// <param name="providerId">프로바이더 ID</param>
        /// <param name="preferLarge">대형 모델 선호 여부</param>
        public string? GetDefaultModel(string providerId, bool preferLarge = false)
        {
            var descriptor = Get(providerId);
            if (descriptor == null) return null;

            return preferLarge ? descriptor.DefaultModels.Large : descriptor.DefaultModels.Small;
        }

        /// <summary>
        /// 특정 라우팅 카테고리를 지원하는 프로바이더 목록을 반환합니다.
        /// </summary>
        public IReadOnlyList<ProviderDescriptor> GetByCategory(RoutingCategory category)
        {
            return _descriptors.Values
                .Where(d => d.SupportedCategories.Contains(category))
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// 프로바이더가 로컬인지 확인합니다.
        /// </summary>
        public bool IsLocal(string providerId)
        {
            return HasCapability(providerId, c => c.Local);
        }

        /// <summary>
        /// 등록된 프로바이더 수를 반환합니다.
        /// </summary>
        public int Count => _descriptors.Count;
    }
}
