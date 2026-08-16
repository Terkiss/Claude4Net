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

        public async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            // Register Mock Provider Factory FIRST so it takes precedence over built-in factories
            services.AddSingleton<IProviderFactory, TestMockProviderFactory>();
            CliServiceRegistration.ConfigureServices(services);

            services.AddSingleton<Claude4NetApiServer>();

            _serviceProvider = services.BuildServiceProvider();
            _server = _serviceProvider.GetRequiredService<Claude4NetApiServer>();

            await _server.StartAsync(TestPort);
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            _client.Dispose();
            if (_serviceProvider != null)
            {
                await _serviceProvider.DisposeAsync();
            }
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
        public async Task ChatCompletions_NonStreaming_ReturnsValidResponse()
        {
            var request = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "Hello test" }
                },
                Stream = false
            };

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{TestPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            var chatResp = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(chatResp);
            Assert.Equal("chat.completion", chatResp.Object);
            Assert.NotEmpty(chatResp.Choices);
            Assert.Equal("assistant", chatResp.Choices[0].Message.Role);
            Assert.NotEmpty(chatResp.Choices[0].Message.GetContentString());
        }

        [Fact]
        public async Task ChatCompletions_Streaming_ReturnsSseChunks()
        {
            var request = new ChatCompletionRequest
            {
                Model = "gemini-2.5-flash",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "Stream this test" }
                },
                Stream = true
            };

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{TestPort}/v1/chat/completions")
            {
                Content = JsonContent.Create(request)
            };

            using var httpResponse = await _client.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
            Assert.Equal("text/event-stream; charset=utf-8", httpResponse.Content.Headers.ContentType?.ToString());

            var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            string? line;
            bool foundData = false;
            bool foundDone = false;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (line.StartsWith("data: [DONE]"))
                {
                    foundDone = true;
                    break;
                }
                if (line.StartsWith("data: {"))
                {
                    foundData = true;
                }
            }

            Assert.True(foundData, "SSE stream should contain data chunks");
            Assert.True(foundDone, "SSE stream should conclude with [DONE]");
        }

        [Fact]
        public void CliOptions_ParsesApiFlagsCorrectly()
        {
            var opts1 = CliOptions.Parse(new[] { "--api", "on", "--api-port", "7836" });
            Assert.True(opts1.StartApi);
            Assert.Equal(7836, opts1.ApiPort);

            var opts2 = CliOptions.Parse(new[] { "--api", "off" });
            Assert.False(opts2.StartApi);

            var opts3 = CliOptions.Parse(new[] { "--api" });
            Assert.True(opts3.StartApi);
            Assert.Equal(7836, opts3.ApiPort);
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

            string stop = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("stop", _serviceProvider);
            Assert.Contains("stopped", stop);

            string statusAfterStop = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("status", _serviceProvider);
            Assert.Contains("STOPPED", statusAfterStop);

            string start = await Claude4Net.Runtime.Handlers.SystemCommands.HandleApi("start 7840", _serviceProvider);
            Assert.Contains("started", start);
            Assert.Contains("7840", start);
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
                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Hello " };
                yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "from in-process Claude4Net API!" };
            }

            public void AddMessage(object message) => _history.Add(message);
            public IReadOnlyList<object> GetHistory() => _history.AsReadOnly();
            public void ClearHistory() => _history.Clear();
            public void SetHistory(IEnumerable<object> history)
            {
                _history.Clear();
                _history.AddRange(history);
            }
        }

        private class TestMockTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => string.IsNullOrEmpty(text) ? 0 : text.Length / 4 + 1;
            public int CountTokens(object message) => 10;
            public int CountTokens(IEnumerable<object> messages) => 50;
        }
    }
}
