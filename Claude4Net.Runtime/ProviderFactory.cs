using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Api;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// LLM ?�로바이?��? ?�성?�기 ?�한 ?�토�??�터?�이?�입?�다.
    /// </summary>
    public interface IProviderFactory
    {
        bool SupportsApiRequests { get; }

        /// <summary>
        /// 지?�된 ?�로바이???�스?�립?��? ?�성?????�는지 ?��?�?결정?�니??
        /// </summary>
        bool CanCreate(ProviderDescriptor descriptor);

        /// <summary>
        /// ?�스?�립???�보�?기반?�로 LLM ?�로바이???�스?�스�??�성?�니??
        /// </summary>
        ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider);

        ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider);

        RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider) =>
            RequestProviderLease.NonOwning(CreateRequestProvider(descriptor, serviceProvider));
    }

    /// <summary>
    /// Anthropic Claude ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class AnthropicProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("anthropic", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<ClaudeService>();
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic");
            return new ClaudeService(new AnthropicClient(httpClient), EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Anthropic");
            var provider = new ClaudeService(new AnthropicClient(httpClient), EmptyToolRegistry.Instance);
            return new RequestProviderLease(provider, httpClient);
        }
    }

    /// <summary>
    /// Google Gemini Native ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class GeminiProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("gemini-native", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<GeminiProvider>();
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini");
            return new GeminiProvider(httpClient, EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini");
            var provider = new GeminiProvider(httpClient, EmptyToolRegistry.Instance);
            return new RequestProviderLease(provider, httpClient);
        }
    }

    /// <summary>
    /// Ollama ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class OllamaProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("openai-compat", StringComparison.OrdinalIgnoreCase) &&
                   descriptor.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<OllamaProvider>();
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama");
            return new OllamaProvider(httpClient, EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("Ollama");
            var provider = new OllamaProvider(httpClient, EmptyToolRegistry.Instance);
            return new RequestProviderLease(provider, httpClient);
        }
    }

    /// <summary>
    /// Gemini CLI 프로바이더 생성을 담당하는 팩토리입니다.
    /// </summary>
    public class GeminiCliProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.Id.Equals("gemini-cli", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<GeminiCliProvider>();
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new GeminiCliProvider(EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            var provider = CreateRequestProvider(descriptor, serviceProvider);
            return RequestProviderLease.NonOwning(provider);
        }
    }

    /// <summary>
    /// Antigravity CLI 프로바이더 생성을 담당하는 팩토리입니다.
    /// </summary>
    public class AntigravityCliProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.Id.Equals("antigravity-cli", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<Claude4Net.Api.AntigravityCliProvider>();
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return new Claude4Net.Api.AntigravityCliProvider(EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            var provider = CreateRequestProvider(descriptor, serviceProvider);
            return RequestProviderLease.NonOwning(provider);
        }
    }

    /// <summary>
    /// Zhipu AI GLM 프로바이더 생성을 담당하는 팩토리입니다.
    /// GLM은 OpenAI 호환이지만 전용 <see cref="GlmProvider"/>를 사용하여
    /// 기본 엔드포인트·모델명을 자동 적용합니다.
    /// </summary>
    public class GlmProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.Id.Equals("glm", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("glm");
            var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();

            return new GlmProvider(httpClient, toolRegistry);
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("glm");
            return new GlmProvider(httpClient, EmptyToolRegistry.Instance);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("glm");
            var provider = new GlmProvider(httpClient, EmptyToolRegistry.Instance);
            return new RequestProviderLease(provider, httpClient);
        }
    }

    /// <summary>
    /// 일반 OpenAI 호환 API 프로바이더 생성을 담당하는 팩토리입니다.
    /// </summary>
    public class OpenAiCompatProviderFactory : IProviderFactory
    {
        public bool SupportsApiRequests => true;

        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("openai-compat", StringComparison.OrdinalIgnoreCase) &&
                   !descriptor.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            ValidateDescriptor(descriptor);

            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiCompat");
            var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();

            return new OpenAiCompatProvider(httpClient, toolRegistry, descriptor);
        }

        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            ValidateDescriptor(descriptor);
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiCompat");
            return new OpenAiCompatProvider(httpClient, EmptyToolRegistry.Instance, descriptor);
        }

        public RequestProviderLease CreateRequestProviderLease(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            ValidateDescriptor(descriptor);
            var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiCompat");
            var provider = new OpenAiCompatProvider(httpClient, EmptyToolRegistry.Instance, descriptor);
            return new RequestProviderLease(provider, httpClient);
        }

        private static void ValidateDescriptor(ProviderDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (string.IsNullOrWhiteSpace(descriptor.Endpoint))
                throw new ArgumentException("Endpoint cannot be empty for OpenAI-compatible provider.", nameof(descriptor));
            ProviderEndpointPolicy.ParseAndValidate(descriptor.Endpoint, nameof(descriptor));

            if (descriptor.Auth?.Mode.Equals("api-key", StringComparison.OrdinalIgnoreCase) == true)
            {
                if (descriptor.Auth.EnvVars == null || descriptor.Auth.EnvVars.Count == 0)
                    throw new ArgumentException("API key authorization mode requires at least one environment variable defined.", nameof(descriptor));
                bool hasKey = descriptor.Auth.EnvVars.Any(envVar =>
                    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)) ||
                    !string.IsNullOrEmpty(AuthManager.GetApiKey(descriptor.Id)) ||
                    !string.IsNullOrEmpty(AuthManager.GetApiKey(envVar)));
                if (!hasKey)
                    throw new InvalidOperationException($"Missing required API key environment variable or key store entry for provider '{descriptor.Id}'.");
            }
            else if (descriptor.Auth is not null &&
                !descriptor.Auth.Mode.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                !descriptor.Auth.Mode.Equals("oauth", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Unsupported authorization mode '{descriptor.Auth.Mode}' for OpenAI-compatible provider.", nameof(descriptor));
            }
        }
    }
}

