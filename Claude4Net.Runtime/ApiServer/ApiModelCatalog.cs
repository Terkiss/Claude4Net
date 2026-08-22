using System.Collections.Frozen;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.SDK;

namespace Claude4Net.Runtime.ApiServer;

internal sealed class ApiModelCatalog
{
    private readonly FrozenDictionary<string, ChatModelRoute> _chatRoutes;
    private readonly FrozenDictionary<string, IEmbeddingProvider> _embeddingRoutes;
    private readonly FrozenDictionary<string, ModelCardDto> _modelCards;

    private ApiModelCatalog(
        Dictionary<string, ChatModelRoute> chatRoutes,
        Dictionary<string, IEmbeddingProvider> embeddingRoutes,
        Dictionary<string, ModelCardDto> modelCards)
    {
        _chatRoutes = chatRoutes.ToFrozenDictionary(StringComparer.Ordinal);
        _embeddingRoutes = embeddingRoutes.ToFrozenDictionary(StringComparer.Ordinal);
        _modelCards = modelCards.ToFrozenDictionary(StringComparer.Ordinal);
        Models = _modelCards.Values.OrderBy(card => card.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<ModelCardDto> Models { get; }
    public bool HasEmbeddingProviders => _embeddingRoutes.Count > 0;

    public bool TryGetChatRoute(string modelId, out ChatModelRoute route) =>
        _chatRoutes.TryGetValue(modelId, out route!);

    public bool TryGetEmbeddingProvider(string modelId, out IEmbeddingProvider provider) =>
        _embeddingRoutes.TryGetValue(modelId, out provider!);

    public bool TryGetModel(string modelId, out ModelCardDto card) =>
        _modelCards.TryGetValue(modelId, out card!);

    public static ApiModelCatalog Build(
        IServiceProvider serviceProvider,
        ProviderRegistry? registry,
        IEnumerable<IEmbeddingProvider> providers)
    {
        var chatRoutes = new Dictionary<string, ChatModelRoute>(StringComparer.Ordinal);
        var embeddingRoutes = new Dictionary<string, IEmbeddingProvider>(StringComparer.Ordinal);
        var modelCards = new Dictionary<string, ModelCardDto>(StringComparer.Ordinal);

        if (registry != null)
        {
            foreach (ProviderDescriptor descriptor in registry.GetApiRequestDescriptors(serviceProvider))
            {
                foreach (string modelId in GetDescriptorModels(descriptor))
                {
                    if (chatRoutes.ContainsKey(modelId))
                    {
                        continue;
                    }

                    chatRoutes[modelId] = new ChatModelRoute(descriptor.Id, modelId);
                    AddModelCard(modelCards, modelId, descriptor.Id);
                }
            }
        }

        foreach (IEmbeddingProvider provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderId) || string.IsNullOrWhiteSpace(provider.ModelId))
            {
                throw new InvalidOperationException("Embedding providers must declare nonblank ProviderId and ModelId values.");
            }

            if (!embeddingRoutes.TryAdd(provider.ModelId, provider))
            {
                throw new InvalidOperationException($"Embedding model '{provider.ModelId}' is registered more than once.");
            }

            AddModelCard(modelCards, provider.ModelId, provider.ProviderId);
        }

        return new ApiModelCatalog(chatRoutes, embeddingRoutes, modelCards);
    }

    private static IEnumerable<string> GetDescriptorModels(ProviderDescriptor descriptor)
    {
        var list = new List<string>();
        if (descriptor.Id.Equals("antigravity-cli", StringComparison.OrdinalIgnoreCase))
        {
            list.AddRange(new[]
            {
                // Primary IDs with clear Antigravity prefix
                "antigravity/gemini-3.7-flash-high",
                "antigravity/gemini-3.7-flash-medium",
                "antigravity/gemini-3.7-flash-low",
                "antigravity/gemini-3.6-flash-high",
                "antigravity/gemini-3.6-flash-medium",
                "antigravity/gemini-3.6-flash-low",
                "antigravity/gemini-3.5-flash-high",
                "antigravity/gemini-3.5-flash-medium",
                "antigravity/gemini-3.5-flash-low",
                "antigravity/gemini-3.1-pro-high",
                "antigravity/gemini-3.1-pro-low",
                "antigravity/claude-sonnet-4-6-thinking",
                "antigravity/claude-opus-4-6-thinking",
                "antigravity/gpt-oss-120b-high",
                "antigravity/gpt-oss-120b-medium",
                // Backward compatibility aliases
                "gemini-3.7-flash-high",
                "gemini-3.7-flash-medium",
                "gemini-3.7-flash-low",
                "gemini-3.6-flash-high",
                "gemini-3.6-flash-medium",
                "gemini-3.6-flash-low",
                "gemini-3.5-flash-high",
                "gemini-3.5-flash-medium",
                "gemini-3.5-flash-low",
                "gemini-3.1-pro-high",
                "gemini-3.1-pro-low",
                "claude-sonnet-4-6-thinking",
                "claude-opus-4-6-thinking",
                "gpt-oss-120b-high",
                "gpt-oss-120b-medium",
                "Gemini 3.7 Flash (High)",
                "Gemini 3.7 Flash (Medium)",
                "Gemini 3.7 Flash (Low)",
                "Gemini 3.6 Flash (High)",
                "Gemini 3.6 Flash (Medium)",
                "Gemini 3.6 Flash (Low)",
                "Gemini 3.5 Flash (High)",
                "Gemini 3.5 Flash (Medium)",
                "Gemini 3.5 Flash (Low)",
                "Gemini 3.1 Pro (High)",
                "Gemini 3.1 Pro (Low)",
                "Claude Sonnet 4.6 (Thinking)",
                "Claude Opus 4.6 (Thinking)",
                "GPT-OSS 120B (High)",
                "GPT-OSS 120B (Medium)"
            });
        }
        else if (descriptor.Id.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            list.AddRange(new[]
            {
                // Primary IDs with clear Google API prefix
                "google/gemini-3.7-flash",
                "google/gemini-3.6-flash",
                "google/gemini-3.5-flash",
                "google/gemini-3.5-flash-lite",
                "google/gemini-3.1-pro",
                "google/gemini-2.5-pro",
                "google/gemini-2.5-flash",
                "google/gemini-2.0-flash",
                "google/gemini-2.0-flash-lite",
                "google/gemini-1.5-pro",
                "google/gemini-1.5-flash",
                // Backward compatibility aliases
                "gemini-3.7-flash",
                "gemini-3.6-flash",
                "gemini-3.5-flash",
                "gemini-3.5-flash-lite",
                "gemini-3.1-pro",
                "gemini-2.5-pro",
                "gemini-2.5-flash",
                "gemini-2.0-flash",
                "gemini-2.0-flash-lite",
                "gemini-1.5-pro",
                "gemini-1.5-flash"
            });
        }
        else if (descriptor.Id.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            list.AddRange(new[]
            {
                // Primary IDs with clear Anthropic prefix
                "anthropic/claude-3-5-sonnet-20241022",
                "anthropic/claude-3-5-haiku-20241022",
                "anthropic/claude-3-opus-20240229",
                // Backward compatibility aliases
                "claude-3-5-sonnet-20241022",
                "claude-3-5-haiku-20241022",
                "claude-3-opus-20240229"
            });
        }
        else if (descriptor.Id.Equals("glm", StringComparison.OrdinalIgnoreCase))
        {
            list.AddRange(new[]
            {
                "glm/glm-4-plus",
                "glm/glm-4-flash",
                "glm/glm-4-air",
                "glm-4-plus",
                "glm-4-flash",
                "glm-4-air"
            });
        }
        else if (descriptor.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            list.AddRange(new[]
            {
                "ollama/llama3",
                "llama3"
            });
        }
        else
        {
            list.Add(descriptor.DefaultModels.Small);
            list.Add(descriptor.DefaultModels.Large);
        }
        return list.Distinct(StringComparer.Ordinal);
    }

    private static void AddModelCard(
        Dictionary<string, ModelCardDto> modelCards,
        string modelId,
        string providerId)
    {
        if (modelCards.ContainsKey(modelId))
        {
            return;
        }

        modelCards.Add(modelId, new ModelCardDto { Id = modelId, OwnedBy = providerId });
    }
}

internal sealed record ChatModelRoute(string ProviderId, string ModelId);
