using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Claude4Net.SDK;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// ?꾨줈諛붿씠???붿뒪?щ┰?곕? 愿由ы븯???덉??ㅽ듃由ъ엯?덈떎.
    /// 湲곗〈 SmartRouter, Doctor, Dashboard?먯꽌 ?섎뱶肄붾뵫?섎뜕 ?꾨줈諛붿씠??硫뷀??곗씠?곕?
    /// 以묒븰?먯꽌 ?좎뼵?곸쑝濡?愿由ы빀?덈떎.
    /// </summary>
    public class ProviderRegistry
    {
        private readonly ConcurrentDictionary<string, ProviderDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);

        internal static Func<string> UserProvidersDirResolver { get; set; } = () =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude4net", "providers");

        /// <summary>
        /// ?깅줉??紐⑤뱺 ?꾨줈諛붿씠???붿뒪?щ┰?곕? 諛섑솚?⑸땲??
        /// </summary>
        public IReadOnlyCollection<ProviderDescriptor> All => _descriptors.Values.ToList().AsReadOnly();

        /// <summary>
        /// 湲곕낯 ?댁옣 ?꾨줈諛붿씠???붿뒪?щ┰?곕? 濡쒕뱶?섏뿬 珥덇린?뷀빀?덈떎.
        /// </summary>
        private void RegisterBuiltInDefaults()
        {
            Register(new ProviderDescriptor
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

            Register(new ProviderDescriptor
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

            Register(new ProviderDescriptor
            {
                Id = "ollama",
                Label = "Ollama (Local)",
                TransportKind = "openai-compat",
                Endpoint = "http://localhost:11434",
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
                ContextWindowSize = Claude4Net.Api.OllamaProvider.GetEffectiveContextLimit()
            });

            Register(new ProviderDescriptor
            {
                Id = "lmstudio",
                Label = "LM Studio (Local)",
                TransportKind = "openai-compat",
                Endpoint = "http://localhost:1234",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "local-model",
                    Large = "local-model"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = false,
                    ThoughtSignature = false,
                    Streaming = true,
                    Embeddings = true,
                    Local = true
                },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { "LMSTUDIO_API_KEY" }
                },
                CostScore = 0.0,
                SupportedCategories = new[]
                {
                    RoutingCategory.CheapUtility,
                    RoutingCategory.QuickFix
                },
                ContextWindowSize = 8192
            });


            Register(new ProviderDescriptor
            {
                Id = "glm",
                Label = "Zhipu GLM (智谱清言)",
                TransportKind = "openai-compat",
                Endpoint = Claude4Net.Api.GlmProvider.DefaultEndpoint,
                DefaultModels = new ProviderDefaultModels
                {
                    Small = Claude4Net.Api.GlmProvider.DefaultSmallModel,
                    Large = Claude4Net.Api.GlmProvider.DefaultLargeModel
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = true,
                    ThoughtSignature = false,
                    Streaming = true,
                    Embeddings = true,
                    Local = false
                },
                Auth = new ProviderAuth
                {
                    Mode = "api-key",
                    EnvVars = new[] { "ZHIPUAI_API_KEY", "GLM_API_KEY" }
                },
                CostScore = 0.3,
                SupportedCategories = new[]
                {
                    RoutingCategory.QuickFix,
                    RoutingCategory.DeepCode,
                    RoutingCategory.Planner,
                    RoutingCategory.Verifier,
                    RoutingCategory.CheapUtility
                },
                ContextWindowSize = Claude4Net.Api.GlmProvider.DefaultContextWindowSize
            });

            Register(new ProviderDescriptor
            {
                Id = "gemini-cli",
                Label = "Gemini CLI (파기 - 오래된 버전, antigravity-cli 권장)",
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
                    RoutingCategory.CheapUtility
                },
                ContextWindowSize = 1_000_000
            });

            Register(new ProviderDescriptor
            {
                Id = "antigravity-cli",
                Label = "Antigravity CLI (Local Agent)",
                TransportKind = "cli",
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "gemini-3.7-flash-high",
                    Large = "gemini-3.1-pro-high"
                },
                Capabilities = new ProviderCapabilities
                {
                    ToolCalling = true,
                    Vision = true,
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
        }

        public static ProviderRegistry CreateWithDefaults()
        {
            return CreateWithDefaults(null);
        }

        public static ProviderRegistry CreateWithDefaults(string? workspaceDir)
        {
            var registry = new ProviderRegistry();
            registry.RegisterBuiltInDefaults();

            // System descriptors: {AppState.SystemBaseDir}/providers
            string systemPath = System.IO.Path.Combine(AppState.SystemBaseDir, "providers");
            registry.LoadFromDirectory(systemPath);

            // User descriptors: %USERPROFILE%/.claude4net/providers
            string userPath = UserProvidersDirResolver();
            registry.LoadFromDirectory(userPath);

            // Workspace descriptors: {workspace}/.claude4net/providers
            string ws = workspaceDir ?? (AppState.CurrentCwd ?? AppState.OriginalCwd);
            string workspacePath = System.IO.Path.Combine(ws, ".claude4net", "providers");
            registry.LoadFromDirectory(workspacePath);

            return registry;
        }

        /// <summary>
        /// 프로바이더 디스크립터를 지정한 디렉토리에서 JSON 파일들로 로드합니다.
        /// 파싱 오류 또는 유효성 검사 실패 시 예외를 던집니다 (Fail-Closed).
        /// </summary>
        public void LoadFromDirectory(string path)
        {
            if (!System.IO.Directory.Exists(path)) return;
            var files = System.IO.Directory.GetFiles(path, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var json = System.IO.File.ReadAllText(file);
                    var descriptor = System.Text.Json.JsonSerializer.Deserialize<ProviderDescriptor>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (descriptor != null)
                    {
                        RegisterInternal(descriptor, file);
                    }
                    else
                    {
                        throw new InvalidOperationException("Parsed descriptor was null.");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to load provider descriptor from file '{file}': {ex.Message}", ex);
                }
            }
        }

        public void Register(ProviderDescriptor descriptor)
        {
            RegisterInternal(descriptor, null);
        }

        private void RegisterInternal(ProviderDescriptor descriptor, string? filePath)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            string context = string.IsNullOrEmpty(filePath)
                ? $"provider ID '{descriptor.Id}'"
                : $"file '{filePath}' (provider ID '{descriptor.Id}')";

            if (string.IsNullOrWhiteSpace(descriptor.Id))
                throw new ArgumentException($"Provider descriptor ID cannot be empty. Context: {context}", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Label))
                throw new ArgumentException($"Provider descriptor label cannot be empty. Context: {context}", nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.TransportKind))
                throw new ArgumentException($"Provider descriptor transport kind cannot be empty. Context: {context}", nameof(descriptor));

            // DefaultModels validation
            if (descriptor.DefaultModels == null ||
                string.IsNullOrWhiteSpace(descriptor.DefaultModels.Small) ||
                string.IsNullOrWhiteSpace(descriptor.DefaultModels.Large))
            {
                throw new ArgumentException($"Provider descriptor default models (both Small and Large) must be specified. Context: {context}", nameof(descriptor));
            }

            // Endpoint validation
            if (!string.IsNullOrWhiteSpace(descriptor.Endpoint))
            {
                ProviderEndpointPolicy.ParseAndValidate(descriptor.Endpoint, "Endpoint");
            }

            // Ensure non-null collections for Headers and Metadata
            var finalDescriptor = descriptor;
            if (descriptor.Headers == null || descriptor.Metadata == null)
            {
                finalDescriptor = descriptor with
                {
                    Headers = descriptor.Headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    Metadata = descriptor.Metadata ?? new Dictionary<string, object?>()
                };
            }

            _descriptors[finalDescriptor.Id] = finalDescriptor;
        }

        /// <summary>
        /// ?꾨줈諛붿씠??ID濡??붿뒪?щ┰?곕? 議고쉶?⑸땲??
        /// </summary>
        public ProviderDescriptor? Get(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            _descriptors.TryGetValue(providerId, out var descriptor);
            return descriptor;
        }

        /// <summary>
        /// ?꾨줈諛붿씠?붽? ?뱀젙 湲곕뒫??吏?먰븯?붿? ?뺤씤?⑸땲??
        /// </summary>
        public bool HasCapability(string providerId, Func<ProviderCapabilities, bool> check)
        {
            var descriptor = Get(providerId);
            return descriptor != null && check(descriptor.Capabilities);
        }

        /// <summary>
        /// ?꾨줈諛붿씠??ID濡?湲곕낯 紐⑤뜽??媛?몄샃?덈떎.
        /// </summary>
        /// <param name="providerId">?꾨줈諛붿씠??ID</param>
        /// <param name="preferLarge">???紐⑤뜽 ?좏샇 ?щ?</param>
        public string? GetDefaultModel(string providerId, bool preferLarge = false)
        {
            var descriptor = Get(providerId);
            if (descriptor == null) return null;

            return preferLarge ? descriptor.DefaultModels.Large : descriptor.DefaultModels.Small;
        }

        /// <summary>
        /// ?뱀젙 ?쇱슦??移댄뀒怨좊━瑜?吏?먰븯???꾨줈諛붿씠??紐⑸줉??諛섑솚?⑸땲??
        /// </summary>
        public IReadOnlyList<ProviderDescriptor> GetByCategory(RoutingCategory category)
        {
            return _descriptors.Values
                .Where(d => d.SupportedCategories.Contains(category))
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// ?꾨줈諛붿씠?붽? 濡쒖뺄?몄? ?뺤씤?⑸땲??
        /// </summary>
        public bool IsLocal(string providerId)
        {
            return HasCapability(providerId, c => c.Local);
        }

        /// <summary>
        /// 등록된 프로바이더 개수를 반환합니다.
        /// </summary>
        public int Count => _descriptors.Count;

        private readonly List<IProviderFactory> _factories = new();

        /// <summary>
        /// 프로바이더 팩토리를 직접 등록합니다.
        /// </summary>
        public void RegisterFactory(IProviderFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _factories.Add(factory);
        }

        /// <summary>
        /// 프로바이더 ID와 서비스 프로바이더를 이용하여 팩토리를 통해 적절한 ILLMProvider를 생성합니다.
        /// 등록된 팩토리가 없거나 생성이 안되는 경우 레거시 분기로 fallback합니다.
        /// </summary>
        public ILLMProvider CreateProvider(string providerId, IServiceProvider serviceProvider)
        {
            var descriptor = Get(providerId);
            if (descriptor != null)
            {
                // 1. DI에 등록된 IProviderFactory 목록에서 확인
                var diFactories = serviceProvider.GetService<IEnumerable<IProviderFactory>>();
                if (diFactories != null)
                {
                    var factory = diFactories.FirstOrDefault(f => f.CanCreate(descriptor));
                    if (factory != null)
                    {
                        return factory.Create(descriptor, serviceProvider);
                    }
                }

                // 2. 직접 등록된 팩토리 목록에서 확인
                var localFactory = _factories.FirstOrDefault(f => f.CanCreate(descriptor));
                if (localFactory != null)
                {
                    return localFactory.Create(descriptor, serviceProvider);
                }
            }

            // 3. Fallback: 기존 레거시 생성 로직
            return CreateProviderLegacy(providerId, serviceProvider);
        }

        public IEnumerable<ProviderDescriptor> GetApiRequestDescriptors(IServiceProvider? serviceProvider = null)
        {
            foreach (var descriptor in All)
            {
                var factory = _factories.FirstOrDefault(f => f.CanCreate(descriptor));
                if (factory == null || factory.SupportsApiRequests)
                {
                    yield return descriptor;
                }
            }
        }

        public RequestProviderLease CreateRequestProviderLease(string providerId, IServiceProvider serviceProvider)
        {
            var descriptor = Get(providerId);
            if (descriptor == null)
            {
                throw new KeyNotFoundException($"Provider '{providerId}' is not registered.");
            }

            var diFactories = serviceProvider.GetService<IEnumerable<IProviderFactory>>();
            var factory = diFactories?.FirstOrDefault(f => f.CanCreate(descriptor)) ??
                          _factories.FirstOrDefault(f => f.CanCreate(descriptor));

            if (factory != null)
            {
                return factory.CreateRequestProviderLease(descriptor, serviceProvider);
            }

            var provider = CreateProviderLegacy(providerId, serviceProvider);
            return RequestProviderLease.NonOwning(provider);
        }

        private ILLMProvider CreateProviderLegacy(string providerId, IServiceProvider serviceProvider)
        {
            return providerId.ToLower() switch
            {
                "gemini" => (ILLMProvider)serviceProvider.GetRequiredService<Claude4Net.Api.GeminiProvider>(),
                "gemini-cli" => (ILLMProvider)serviceProvider.GetRequiredService<Claude4Net.Api.GeminiCliProvider>(),
                "antigravity-cli" => (ILLMProvider)serviceProvider.GetRequiredService<Claude4Net.Api.AntigravityCliProvider>(),
                "ollama" => (ILLMProvider)serviceProvider.GetRequiredService<Claude4Net.Api.OllamaProvider>(),
                _ => (ILLMProvider)serviceProvider.GetRequiredService<Claude4Net.Api.ClaudeService>()
            };
        }
    }
}
