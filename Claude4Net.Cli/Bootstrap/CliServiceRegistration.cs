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
/// Claude4Net CLI에서 사용하는 서비스를 등록합니다.
/// </summary>
public static class CliServiceRegistration
{
    /// <summary>
    /// 애플리케이션의 의존성 주입 서비스를 구성합니다.
    /// </summary>
    public static void ConfigureServices(IServiceCollection services)
    {
        AppState.LoadDiscordApprovers();

        // 외부 API 호출에 사용하는 HTTP 클라이언트
        services.AddHttpClient();

        // 런타임 서비스
        services.AddSingleton<ProviderRegistry>(sp => ProviderRegistry.CreateWithDefaults());
        services.AddSingleton<HookPipeline>();
        services.AddSingleton<AuditTrailService>(sp => new AuditTrailService(maxEntries: 100));
        services.AddSingleton<MemoryStrategyManager>(sp => MemoryStrategyManager.CreateWithDefaults());

        // 메시징 서비스
        services.AddSingleton<IInputBroker, ChannelBroker>();

        // Discord 연동
        services.AddSingleton<DiscordListenerService>();

        // 도구 레지스트리와 도구 서비스
        services.AddSingleton<LspClient>();
        services.AddSingleton<ITool, LspTool>();
        services.AddSingleton<ITool, BashTool>();
        services.AddSingleton<ITool, FileReadTool>();
        services.AddSingleton<ITool, FileWriteTool>();
        services.AddSingleton<ITool, FileEditTool>();
        services.AddSingleton<ITool, LsTool>();

        // 런타임 핵심 구성 요소
        services.AddSingleton<ISmartRouter, SmartRouter>();
        services.AddSingleton<IUserApprovalHandler, CliUserApprovalHandler>();

        // 스킬 레지스트리
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

        // LLM provider 등록
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
        services.AddSingleton<Claude4Net.Api.AntigravityCliProvider>();
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

        // provider factory 등록
        services.AddSingleton<IProviderFactory, AnthropicProviderFactory>();
        services.AddSingleton<IProviderFactory, GeminiProviderFactory>();
        services.AddSingleton<IProviderFactory, OllamaProviderFactory>();
        services.AddSingleton<IProviderFactory, GeminiCliProviderFactory>();
        services.AddSingleton<IProviderFactory, AntigravityCliProviderFactory>();
        services.AddSingleton<IProviderFactory, OpenAiCompatProviderFactory>();
    }
}
