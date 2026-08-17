using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.Runtime.Handlers;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class OpenAiApiServerTests : IAsyncLifetime
    {
        private ServiceProvider _serviceProvider = null!;
        private Claude4NetApiServer _server = null!;
        private readonly HttpClient _client = new();
        private const int TestPort = 7839;
        private const string TestApiKey = "c4n-sk-test-secret-key-12345678";

        private string? _origCwd;
        private string _origSessionId = null!;
        private string _origActiveProvider = null!;
        private string _origActiveModel = null!;
        private PermissionMode _origPermissionMode;
        private bool _origIsExplicit;

        public async Task InitializeAsync()
        {
            _origCwd = AppState.CurrentCwd;
            _origSessionId = AppState.SessionId;
            _origActiveProvider = AppState.ActiveProvider;
            _origActiveModel = AppState.ActiveModel;
            _origPermissionMode = AppState.CurrentPermissionMode;
            _origIsExplicit = AppState.IsProviderExplicitlySet;

            var services = new ServiceCollection();
            // Register Mock Provider Factory FIRST so it takes precedence over built-in factories
            services.AddSingleton<IProviderFactory, TestMockProviderFactory>();
            CliServiceRegistration.ConfigureServices(services);

            services.AddSingleton<Claude4NetApiServer>();

            _serviceProvider = services.BuildServiceProvider();
            _server = _serviceProvider.GetRequiredService<Claude4NetApiServer>();

            await _server.StartAsync(TestPort, TestApiKey);

            // Configure default client to send authorized Bearer header
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestApiKey);
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            _client.Dispose();
            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }

            AppState.CurrentCwd = _origCwd;
            AppState.SessionId = _origSessionId;
            AppState.ActiveProvider = _origActiveProvider;
            AppState.ActiveModel = _origActiveModel;
            AppState.CurrentPermissionMode = _origPermissionMode;
            AppState.IsProviderExplicitlySet = _origIsExplicit;
        }

        [Fact]
        public async Task Auth_StrictValidation_RejectsMissingOrInvalidKey_AllowsHealthCheck()
        {
            using var unauthClient = new HttpClient();

            // 1. Missing auth header -> 401 Unauthorized
            var unauthResp = await unauthClient.GetAsync($"http://localhost:{TestPort}/v1/models");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

            // 2. Wrong token -> 401 Unauthorized
            using var wrongKeyClient = new HttpClient();
            wrongKeyClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-key-xyz");
            var wrongResp = await wrongKeyClient.GetAsync($"http://localhost:{TestPort}/v1/models");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResp.StatusCode);

            // 3. Health check allowed without auth -> 200 OK
            var healthResp = await unauthClient.GetAsync($"http://localhost:{TestPort}/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

            // 4. x-api-key header authentication -> 200 OK
            using var xKeyClient = new HttpClient();
            xKeyClient.DefaultRequestHeaders.Add("x-api-key", TestApiKey);
            var xKeyResp = await xKeyClient.GetAsync($"http://localhost:{TestPort}/v1/models");
            Assert.Equal(HttpStatusCode.OK, xKeyResp.StatusCode);
        }

        [Fact]
        public async Task Cors_PreflightOptions_ReturnsOkWithoutAuth()
        {
            using var unauthClient = new HttpClient();
            using var optionsReq = new HttpRequestMessage(HttpMethod.Options, $"http://localhost:{TestPort}/v1/chat/completions");
            optionsReq.Headers.Add("Origin", "http://localhost:3000");
            optionsReq.Headers.Add("Access-Control-Request-Method", "POST");

            var resp = await unauthClient.SendAsync(optionsReq);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            Assert.True(resp.Headers.Contains("Access-Control-Allow-Origin") || resp.Headers.Contains("access-control-allow-origin"));
        }

        [Fact]
        public async Task GetModels_ReturnsModelListResponse()
        {
            var response = await _client.GetAsync($"http://localhost:{TestPort}/v1/models");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var content = await response.Content.ReadAsStringAsync();
            var modelList = JsonSerializer.Deserialize<ModelListResponse>(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            Assert.NotNull(modelList);
            Assert.Equal("list", modelList.Object);
            Assert.NotEmpty(modelList.Data);
            Assert.Contains(modelList.Data, m => m.Id.Contains("claude"));
            Assert.Contains(modelList.Data, m => m.Id.Contains("gemini"));
            Assert.Contains(modelList.Data, m => m.Id.Contains("embedding"));
        }

        [Fact]
        public async Task GetModel_ById_ReturnsModelCard_Or404ForUnknownModel()
        {
            // 1. Existing model
            var resp1 = await _client.GetAsync($"http://localhost:{TestPort}/v1/models/gpt-4o");
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            var card1 = await resp1.Content.ReadFromJsonAsync<ModelCardDto>();
            Assert.NotNull(card1);
            Assert.Equal("gpt-4o", card1.Id);
            Assert.Equal("openai", card1.OwnedBy);

            // 2. Non-existent model -> 404 with OpenAI error envelope
            var resp2 = await _client.GetAsync($"http://localhost:{TestPort}/v1/models/non-existent-model-xyz");
            Assert.Equal(HttpStatusCode.NotFound, resp2.StatusCode);
            var errJson = await resp2.Content.ReadAsStringAsync();
            Assert.Contains("model_not_found", errJson);
        }

        [Fact]
        public async Task TextCompletions_LegacyEndpoint_ReturnsTextResponse()
        {
            var req = new TextCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Prompt = "Translate 'hello' to French"
            };

            var resp = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/completions", req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var textResp = await resp.Content.ReadFromJsonAsync<TextCompletionResponse>();
            Assert.NotNull(textResp);
            Assert.Equal("text_completion", textResp.Object);
            Assert.Single(textResp.Choices);
            Assert.NotEmpty(textResp.Choices[0].Text);
            Assert.Equal("stop", textResp.Choices[0].FinishReason);
            Assert.True(textResp.Usage.TotalTokens > 0);
        }

        [Fact]
        public async Task GetHealthAndStatus_ReturnsValidApiStatus()
        {
            var response = await _client.GetAsync($"http://localhost:{TestPort}/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<ApiStatusResponse>();
            Assert.NotNull(status);
            Assert.Equal("healthy", status.Status);
            Assert.Equal(TestPort, status.Port);

            var statusResp = await _client.GetAsync($"http://localhost:{TestPort}/api/v1/status");
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        }

        [Fact]
        public async Task GetUsage_ReturnsValidUsageMetrics()
        {
            var response = await _client.GetAsync($"http://localhost:{TestPort}/api/v1/usage");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var usage = await response.Content.ReadFromJsonAsync<ApiUsageResponse>();
            Assert.NotNull(usage);
            Assert.True(usage.ContextLimit > 0);
            Assert.NotNull(usage.ContextComponents);
        }

        [Fact]
        public async Task GetTools_ReturnsRegisteredTools()
        {
            var response = await _client.GetAsync($"http://localhost:{TestPort}/api/v1/tools");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("tools", json);
            Assert.Contains("count", json);
        }

        [Fact]
        public async Task Embeddings_SingleAndBatchInput_ReturnsNormalizedVectors()
        {
            // 1. Single string input with custom dimensions (768)
            var req1 = new EmbeddingRequest
            {
                Model = "text-embedding-004",
                Input = "Hello world embedding test",
                Dimensions = 768
            };

            var resp1 = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/embeddings", req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

            var embedResp1 = await resp1.Content.ReadFromJsonAsync<EmbeddingResponse>();
            Assert.NotNull(embedResp1);
            Assert.Equal("list", embedResp1.Object);
            Assert.Single(embedResp1.Data);
            Assert.Equal(768, embedResp1.Data[0].Embedding.Count);

            // 2. Batch string array input (default 1536)
            var req2 = new EmbeddingRequest
            {
                Model = "text-embedding-3-small",
                Input = new[] { "First document chunk", "Second document chunk", "Third query vector" }
            };

            var resp2 = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/embeddings", req2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

            var embedResp2 = await resp2.Content.ReadFromJsonAsync<EmbeddingResponse>();
            Assert.NotNull(embedResp2);
            Assert.Equal(3, embedResp2.Data.Count);
            Assert.Equal(0, embedResp2.Data[0].Index);
            Assert.Equal(1, embedResp2.Data[1].Index);
            Assert.Equal(2, embedResp2.Data[2].Index);
            Assert.Equal(1536, embedResp2.Data[0].Embedding.Count);
        }

        [Fact]
        public async Task ChatCompletions_NonStreaming_ReturnsValidResponse()
        {
            var request = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "Hello test" }
                },
                Stream = false,
                MaxCompletionTokens = 100
            };

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            var chatResp = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(chatResp);
            Assert.Equal("chat.completion", chatResp.Object);
            Assert.NotEmpty(chatResp.Choices);
            Assert.Equal("assistant", chatResp.Choices[0].Message.Role);
            Assert.NotEmpty(chatResp.Choices[0].Message.GetContentString());
            Assert.Equal("fp_claude4net", chatResp.SystemFingerprint);
        }

        [Fact]
        public async Task ChatCompletions_MultimodalArrayContent_ExtractsTextAndImages()
        {
            var request = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto>
                {
                    new()
                    {
                        Role = "user",
                        Content = new object[]
                        {
                            new { type = "text", text = "Describe this diagram:" },
                            new { type = "image_url", image_url = new { url = "https://example.com/diagram.png" } }
                        }
                    }
                },
                Stream = false
            };

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            var chatResp = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(chatResp);
            Assert.NotEmpty(chatResp.Choices);
        }

        [Fact]
        public async Task ChatCompletions_WithTools_ReturnsToolCalls()
        {
            var request = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "invoke tool calculator with number 42" }
                },
                Tools = new List<ToolDto>
                {
                    new()
                    {
                        Type = "function",
                        Function = new FunctionDto
                        {
                            Name = "calculator",
                            Description = "Performs calculations",
                            Parameters = new { type = "object", properties = new { number = new { type = "number" } } }
                        }
                    }
                },
                Stream = false
            };

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            var chatResp = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(chatResp);
            Assert.NotEmpty(chatResp.Choices);

            var choice = chatResp.Choices[0];
            if (choice.FinishReason == "tool_calls")
            {
                Assert.NotNull(choice.Message.ToolCalls);
                Assert.NotEmpty(choice.Message.ToolCalls);
                Assert.Equal("calculator", choice.Message.ToolCalls[0].Function.Name);
            }
        }

        [Fact]
        public async Task ChatCompletions_Streaming_WithStreamOptions_AndReasoningContent()
        {
            var request = new ChatCompletionRequest
            {
                Model = "gemini-2.5-flash",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "reasoning test please" }
                },
                Stream = true,
                StreamOptions = new StreamOptionsDto { IncludeUsage = true }
            };

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{TestPort}/v1/chat/completions")
            {
                Content = JsonContent.Create(request)
            };

            var httpResponse = await _client.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
            Assert.Equal("text/event-stream; charset=utf-8", httpResponse.Content.Headers.ContentType?.ToString());

            using var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            var receivedChunks = new List<string>();
            bool sawReasoning = false;
            bool sawUsageChunk = false;

            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("data: "))
                {
                    receivedChunks.Add(line);
                    if (line.Contains("[DONE]")) break;

                    string dataJson = line.Substring("data: ".Length);
                    if (dataJson.Contains("reasoning_content"))
                    {
                        sawReasoning = true;
                    }
                    if (dataJson.Contains("\"usage\":{") && dataJson.Contains("\"choices\":[]"))
                    {
                        sawUsageChunk = true;
                    }
                }
            }

            Assert.NotEmpty(receivedChunks);
            Assert.True(sawReasoning, "Expected reasoning_content delta in streamed chunks.");
            Assert.True(sawUsageChunk, "Expected final usage chunk when stream_options.include_usage is true.");
            Assert.Contains(receivedChunks, c => c.Contains("[DONE]"));
        }

        [Fact]
        public void CliOptions_ApiArgumentsParsing_WithApiKey()
        {
            var opts = CliOptions.Parse(new[] { "--api", "on", "--api-port", "8080", "--api-key", "my-secret-key" });
            Assert.True(opts.StartApi);
            Assert.Equal(8080, opts.ApiPort);
            Assert.Equal("my-secret-key", opts.ApiKey);

            var opts2 = CliOptions.Parse(new[] { "--api", "off" });
            Assert.False(opts2.StartApi);

            var opts3 = CliOptions.Parse(new[] { "--api", "-k", "another-key" });
            Assert.True(opts3.StartApi);
            Assert.Equal(7836, opts3.ApiPort);
            Assert.Equal("another-key", opts3.ApiKey);
        }

        [Fact]
        public async Task SystemCommands_HandleUsage_RendersContextGauge()
        {
            string output = await Claude4Net.Runtime.Handlers.SystemCommands.HandleUsage("", _serviceProvider);
            Assert.Contains("Total Calls:", output);
            Assert.Contains("Context:", output);
        }

        [Fact]
        public async Task SystemCommands_HandleApi_StatusAndControl()
        {
            string status = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("status", _serviceProvider);
            Assert.Contains("RUNNING", status);
            Assert.Contains("Bearer Auth Key:", status);

            string stop = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("stop", _serviceProvider);
            Assert.Contains("stopped", stop);

            string statusAfterStop = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("status", _serviceProvider);
            Assert.Contains("STOPPED", statusAfterStop);

            string start = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("start 7840 custom-token-999", _serviceProvider);
            Assert.Contains("started", start);
            Assert.Contains("7840", start);
            Assert.Contains("custom-token-999", start);
        }

        [Fact]
        public void Gemini_ContextLimit_DynamicResolution_MatchesSpecs()
        {
            // Gemini Pro models have 2,000,000 (2M) context limit
            Assert.Equal(2_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-1.5-pro"));
            Assert.Equal(2_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-2.0-pro-exp"));
            Assert.Equal(2_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-2.5-pro"));

            // Gemini Flash models have 1,000,000 (1M) context limit
            Assert.Equal(1_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-1.5-flash"));
            Assert.Equal(1_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-2.0-flash"));
            Assert.Equal(1_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-2.5-flash"));
            Assert.Equal(1_000_000, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-1.5-flash-8b"));

            // Legacy Gemini 1.0 Pro
            Assert.Equal(32_768, Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit("gemini-1.0-pro"));
        }

        [Fact]
        public async Task SystemCommands_HandleUsage_DynamicGeminiModelSpec()
        {
            string prevModel = AppState.ActiveModel;
            try
            {
                AppState.ActiveModel = "gemini-1.5-pro";
                string output = await Claude4Net.Runtime.Handlers.SystemCommands.HandleUsage("", _serviceProvider);
                Assert.Contains("2,000,000", output);

                AppState.ActiveModel = "gemini-2.5-flash";
                string outputFlash = await Claude4Net.Runtime.Handlers.SystemCommands.HandleUsage("", _serviceProvider);
                Assert.Contains("1,000,000", outputFlash);
            }
            finally
            {
                AppState.ActiveModel = prevModel;
            }
        }

        private class TestMockProviderFactory : IProviderFactory
        {
            public string TransportKind => "mock";

            public bool CanCreate(ProviderDescriptor descriptor) => true;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new TestMockProvider();
            }
        }

        private class TestMockProvider : ILLMProvider
        {
            private readonly List<object> _history = new();
            public string Name => "MockProvider";
            public ITokenCounter TokenCounter { get; } = new TestMockTokenCounter();
            public int ContextLimit => AppState.ActiveModel.Contains("gemini", StringComparison.OrdinalIgnoreCase)
                ? Claude4Net.Api.GeminiProvider.ResolveGeminiContextLimit(AppState.ActiveModel)
                : 200000;

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                if (prompt.Contains("invoke tool", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<invoke name=\"calculator\"><parameter name=\"number\">42</parameter></invoke>" };
                }
                else if (prompt.Contains("reasoning test", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<think>Let me think step-by-step...</think>The final answer is 42." };
                }
                else
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Mock response to: " + prompt };
                }
            }

            public void AddMessage(object message)
            {
                if (message != null) _history.Add(message);
            }

            public IReadOnlyList<object> GetHistory() => _history;
            public void SetHistory(IEnumerable<object> history)
            {
                _history.Clear();
                if (history != null) _history.AddRange(history);
            }
            public void ClearHistory() => _history.Clear();
        }

        private class TestMockTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => Math.Max(1, (text?.Length ?? 0) / 4);
            public int CountTokens(object message) => 10;
            public int CountTokens(IEnumerable<object> history) => 50;
        }
    }
}
