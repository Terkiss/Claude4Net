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
        /// <summary>
        /// 지?�된 ?�로바이???�스?�립?��? ?�성?????�는지 ?��?�?결정?�니??
        /// </summary>
        bool CanCreate(ProviderDescriptor descriptor);

        /// <summary>
        /// ?�스?�립???�보�?기반?�로 LLM ?�로바이???�스?�스�??�성?�니??
        /// </summary>
        ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider);
    }

    /// <summary>
    /// Anthropic Claude ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class AnthropicProviderFactory : IProviderFactory
    {
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
    }

    /// <summary>
    /// Google Gemini Native ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class GeminiProviderFactory : IProviderFactory
    {
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
    }

    /// <summary>
    /// Ollama ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class OllamaProviderFactory : IProviderFactory
    {
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
    }

    /// <summary>
    /// Gemini CLI ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class GeminiCliProviderFactory : IProviderFactory
    {
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
    }

    /// <summary>
    /// Antigravity CLI ?�로바이???�성???�당?�는 ?�토리입?�다.
    /// </summary>
    public class AntigravityCliProviderFactory : IProviderFactory
    {
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
    }

    /// <summary>
    /// Zhipu AI GLM 프로바이더 생성을 담당하는 팩토리입니다.
    /// GLM은 OpenAI 호환이지만 전용 <see cref="GlmProvider"/>를 사용하여
    /// 기본 엔드포인트·모델명을 자동 적용합니다.
    /// </summary>
    public class GlmProviderFactory : IProviderFactory
    {
        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.Id.Equals("glm", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory != null
                ? httpClientFactory.CreateClient(descriptor.Id)
                : new HttpClient();
            var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();

            return new GlmProvider(httpClient, toolRegistry);
        }
    }

    /// <summary>
    /// 일반 OpenAI 호환 API 프로바이더 생성을 담당하는 팩토리입니다.
    /// </summary>
    public class OpenAiCompatProviderFactory : IProviderFactory
    {
        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("openai-compat", StringComparison.OrdinalIgnoreCase) &&
                   !descriptor.Id.Equals("ollama", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            // Robust endpoint validation
            if (string.IsNullOrWhiteSpace(descriptor.Endpoint))
            {
                throw new ArgumentException("Endpoint cannot be empty for OpenAI-compatible provider.", nameof(descriptor));
            }

            if (!Uri.TryCreate(descriptor.Endpoint, UriKind.Absolute, out var uriResult) ||
                !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
            {
                throw new ArgumentException($"Endpoint '{descriptor.Endpoint}' is not a valid absolute HTTP/HTTPS URI.", nameof(descriptor));
            }

            // Credential checks
            if (descriptor.Auth != null)
            {
                var mode = descriptor.Auth.Mode;
                if (mode.Equals("api-key", StringComparison.OrdinalIgnoreCase))
                {
                    if (descriptor.Auth.EnvVars == null || descriptor.Auth.EnvVars.Count == 0)
                    {
                        throw new ArgumentException("API key authorization mode requires at least one environment variable defined.", nameof(descriptor));
                    }

                    bool hasKey = false;
                    foreach (var envVar in descriptor.Auth.EnvVars)
                    {
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)) ||
                            !string.IsNullOrEmpty(AuthManager.GetApiKey(descriptor.Id)) ||
                            !string.IsNullOrEmpty(AuthManager.GetApiKey(envVar)))
                        {
                            hasKey = true;
                            break;
                        }
                    }

                    if (!hasKey)
                    {
                        throw new InvalidOperationException($"Missing required API key environment variable or key store entry for provider '{descriptor.Id}'.");
                    }
                }
                else if (mode.Equals("oauth", StringComparison.OrdinalIgnoreCase))
                {
                    // OAuth check: if needed, we can expand this. For now just no-op.
                }
                else if (!mode.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Unsupported authorization mode '{mode}' for OpenAI-compatible provider.", nameof(descriptor));
                }
            }

            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory != null ? httpClientFactory.CreateClient(descriptor.Id) : new HttpClient();
            var toolRegistry = serviceProvider.GetRequiredService<IToolRegistry>();

            return new OpenAiCompatProvider(httpClient, toolRegistry, descriptor);
        }
    }
}

