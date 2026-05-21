using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using Claude4Net.SDK;

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

        /// <summary>
        /// ?깅줉??紐⑤뱺 ?꾨줈諛붿씠???붿뒪?щ┰?곕? 諛섑솚?⑸땲??
        /// </summary>
        public IReadOnlyCollection<ProviderDescriptor> All => _descriptors.Values.ToList().AsReadOnly();

        /// <summary>
        /// 湲곕낯 ?댁옣 ?꾨줈諛붿씠???붿뒪?щ┰?곕? 濡쒕뱶?섏뿬 珥덇린?뷀빀?덈떎.
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
                ContextWindowSize = Claude4Net.Api.OllamaProvider.GetEffectiveContextLimit()
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
        /// ?꾨줈諛붿씠???붿뒪?щ┰?곕? ?깅줉?⑸땲??
        /// ID媛 鍮꾩뼱?덇굅???좏슚?섏? ?딆쑝硫??덉쇅瑜?諛쒖깮?쒗궢?덈떎.
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
                        Register(descriptor);
                    }
                }
                catch { }
            }
        }

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
        /// ?깅줉???꾨줈諛붿씠???섎? 諛섑솚?⑸땲??
        /// </summary>
        public int Count => _descriptors.Count;
    }
}
