using System;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Api;
using Claude4Net.Tools;
using Claude4Net.Commands;
using Claude4Net.Discord;

namespace Claude4Net.Cli.Bootstrap;

/// <summary>
/// Service registration for Claude4Net CLI.
/// </summary>
public static class CliServiceRegistration
{
    /// <summary>
    /// Configures services for the application.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        AppState.LoadDiscordApprovers();

        // HTTP Client
        services.AddHttpClient();

        // Runtime Services
        services.AddSingleton<ProviderRegistry>(sp => ProviderRegistry.CreateWithDefaults());
        services.AddSingleton<HookPipeline>();
        services.AddSingleton<AuditTrailService>(sp => new AuditTrailService(maxEntries: 100));
        services.AddSingleton<MemoryStrategyManager>(sp => MemoryStrategyManager.CreateWithDefaults());

        // Messaging
        services.AddSingleton<IInputBroker, ChannelBroker>();

        // Discord
        services.AddSingleton<DiscordListenerService>();

        // Tools
        services.AddSingleton<LspClient>();
        services.AddSingleton<ITool, LspTool>();
        services.AddSingleton<ITool, BashTool>();
        services.AddSingleton<ITool, FileReadTool>();
        services.AddSingleton<ITool, FileWriteTool>();
        services.AddSingleton<ITool, FileEditTool>();
        services.AddSingleton<ITool, LsTool>();

        // Runtime Core
        services.AddSingleton<ISmartRouter, SmartRouter>();
        services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>();

        // Skill Registry
        services.AddSingleton<SkillRegistryService>(sp =>
        {
            string ws = AppState.CurrentCwd ?? AppState.SystemBaseDir;
            return new SkillRegistryService(ws);
        });

        services.AddSingleton<SkillProposalService>(sp =>
        {
            var registry = sp.GetRequiredService<SkillRegistryService>();
            return new SkillProposalService(registry);
        });

        services.AddSingleton<ToolOrchestrator>(sp => new ToolOrchestrator(
            sp.GetServices<ITool>(),
            sp.GetService<IUserApprovalHandler>(),
            sp));
        services.AddSingleton<IToolRegistry>(sp => sp.GetRequiredService<ToolOrchestrator>());

        // LLM Providers
        services.AddSingleton<AnthropicClient>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("Anthropic");
            return new AnthropicClient(httpClient);
        });
        services.AddSingleton<ClaudeService>();
        services.AddSingleton<GeminiProvider>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("Gemini");
            httpClient.Timeout = TimeSpan.FromSeconds(180);
            return new GeminiProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
        });
        services.AddSingleton<GeminiCliProvider>();
        services.AddSingleton<IEmbeddingProvider, GeminiEmbeddingProvider>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("Gemini");
            return new GeminiEmbeddingProvider(httpClient);
        });
        services.AddSingleton<OllamaProvider>(sp =>
        {
            var clientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = clientFactory.CreateClient("Ollama");
            httpClient.Timeout = TimeSpan.FromSeconds(300);
            return new OllamaProvider(httpClient, sp.GetRequiredService<IToolRegistry>());
        });

        // Provider Factories
        services.AddSingleton<IProviderFactory, AnthropicProviderFactory>();
        services.AddSingleton<IProviderFactory, GeminiProviderFactory>();
        services.AddSingleton<IProviderFactory, OllamaProviderFactory>();
        services.AddSingleton<IProviderFactory, GeminiCliProviderFactory>();
        services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();
    }
}
