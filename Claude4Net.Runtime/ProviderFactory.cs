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
    /// LLM 프로바이더를 생성하기 위한 팩토리 인터페이스입니다.
    /// </summary>
    public interface IProviderFactory
    {
        /// <summary>
        /// 지정된 프로바이더 디스크립터를 생성할 수 있는지 여부를 결정합니다.
        /// </summary>
        bool CanCreate(ProviderDescriptor descriptor);

        /// <summary>
        /// 디스크립터 정보를 기반으로 LLM 프로바이더 인스턴스를 생성합니다.
        /// </summary>
        ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider);
    }

    /// <summary>
    /// Anthropic Claude 프로바이더 생성을 담당하는 팩토리입니다.
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
    /// Google Gemini Native 프로바이더 생성을 담당하는 팩토리입니다.
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
    /// Ollama 프로바이더 생성을 담당하는 팩토리입니다.
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
    /// Gemini CLI 프로바이더 생성을 담당하는 팩토리입니다.
    /// </summary>
    public class GeminiCliProviderFactory : IProviderFactory
    {
        public bool CanCreate(ProviderDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.TransportKind.Equals("cli", StringComparison.OrdinalIgnoreCase);
        }

        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            return serviceProvider.GetRequiredService<GeminiCliProvider>();
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

    /// <summary>
    /// OpenAI 규격(/v1/chat/completions)에 맞춰 작동하는 범용 OpenAI 호환 LLM 프로바이더 구현체입니다.
    /// </summary>
    public class OpenAiCompatProvider : ILLMProvider
    {
        private readonly HttpClient _httpClient;
        private readonly IToolRegistry _toolRegistry;
        private readonly ProviderDescriptor _descriptor;
        private readonly List<object> _messageHistory = new();

        public OpenAiCompatProvider(HttpClient httpClient, IToolRegistry toolRegistry, ProviderDescriptor descriptor)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        }

        public string Name => _descriptor.Id;

        public ITokenCounter TokenCounter { get; } = new DefaultTokenCounter();

        public int ContextLimit => _descriptor.ContextWindowSize > 0 ? _descriptor.ContextWindowSize : 200000;

        public void AddMessage(object message)
        {
            if (message != null)
            {
                _messageHistory.Add(message);
            }
        }

        public IReadOnlyList<object> GetHistory() => _messageHistory.AsReadOnly();

        public void SetHistory(IEnumerable<object> history)
        {
            _messageHistory.Clear();
            if (history != null)
            {
                _messageHistory.AddRange(history);
            }
        }

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
            string prompt,
            string? model = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            string actualModel = model ?? _descriptor.DefaultModels.Large;
            if (string.IsNullOrEmpty(actualModel))
            {
                actualModel = _descriptor.DefaultModels.Small;
            }

            if (!string.IsNullOrEmpty(prompt))
            {
                _messageHistory.Add(new { role = "user", content = prompt });
            }

            var systemPrompt = new SystemPromptBuilder().Build(_descriptor.Id);
            var systemMsg = new { role = "system", content = systemPrompt };

            var finalMessages = new List<object> { systemMsg };
            finalMessages.AddRange(_messageHistory);

            var payload = new
            {
                model = actualModel,
                messages = finalMessages,
                stream = true
            };

            string endpoint = _descriptor.Endpoint;
            if (!endpoint.Contains("/chat/completions"))
            {
                string baseAddr = endpoint.TrimEnd('/');
                endpoint = baseAddr.EndsWith("/v1") ? baseAddr + "/chat/completions" : baseAddr + "/v1/chat/completions";
            }

            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            // Set Headers
            if (_descriptor.Headers != null)
            {
                foreach (var header in _descriptor.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Set Authentication
            if (_descriptor.Auth != null && _descriptor.Auth.Mode.Equals("api-key", StringComparison.OrdinalIgnoreCase))
            {
                string? apiKey = null;
                foreach (var envVar in _descriptor.Auth.EnvVars)
                {
                    apiKey = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(apiKey)) break;
                }
                if (string.IsNullOrEmpty(apiKey))
                {
                    apiKey = AuthManager.GetApiKey(_descriptor.Id);
                }
                if (string.IsNullOrEmpty(apiKey) && _descriptor.Auth.EnvVars.Count > 0)
                {
                    apiKey = AuthManager.GetApiKey(_descriptor.Auth.EnvVars[0]);
                }

                if (!string.IsNullOrEmpty(apiKey))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                }
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            var finalRes = new LLMResponse();

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string dataLine = line.Trim();
                if (dataLine.StartsWith("data: "))
                {
                    string jsonStr = dataLine.Substring(6).Trim();
                    if (jsonStr.Equals("[DONE]", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    JsonElement chunk;
                    try
                    {
                        chunk = JsonSerializer.Deserialize<JsonElement>(jsonStr);
                    }
                    catch
                    {
                        continue;
                    }

                    if (chunk.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var choice = choices[0];
                        if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var content))
                        {
                            string text = content.GetString() ?? "";
                            if (!string.IsNullOrEmpty(text))
                            {
                                finalRes.Text += text;
                                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = text };
                            }
                        }
                    }
                }
            }

            _messageHistory.Add(new { role = "assistant", content = finalRes.Text });
            yield return new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = finalRes };
        }
    }
}
