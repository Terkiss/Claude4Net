using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.Runtime.ApiServer.Streaming;
using Claude4Net.Runtime.Services;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests
{
    public class TestMockStreamingProvider : ILLMProvider
    {
        public string Name => "TestStreamingMock";
        public int ContextLimit => 100000;
        public ITokenCounter TokenCounter { get; } = new DummyTokenCounter();

        public List<string> FragmentsToEmit { get; set; } = new();
        public List<LLMStreamEvent> EventsToEmit { get; set; } = new();
        public bool BlockAfterEvents { get; set; }
        public CancellationToken EnumeratorToken { get; private set; }
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EnumeratorDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            EnumeratorToken = ct;
            try
            {
                foreach (var streamEvent in EventsToEmit)
                {
                    await Task.Delay(1, ct);
                    yield return streamEvent;
                }

                foreach (var frag in FragmentsToEmit)
                {
                    await Task.Delay(1, ct);
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = frag };
                }

                if (BlockAfterEvents)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
            }
            finally
            {
                if (ct.IsCancellationRequested) CancellationObserved.TrySetResult();
                EnumeratorDisposed.TrySetResult();
            }
        }

        public Task<string> QueryAsync(string prompt, string? model = null, CancellationToken ct = default) => throw new NotImplementedException();
        public void AddMessage(object msg) { }
        public void AddToolResult(string toolCallId, string result) { }
        public void ClearHistory() { }
        public IReadOnlyList<object> GetHistory() => new List<object>();
        public void SetHistory(IEnumerable<object> history) { }
        
        private class DummyTokenCounter : ITokenCounter {
            public int CountTokens(string text) => text?.Length ?? 0;
            public int CountTokens(object msg) => 0;
            public int CountTokens(IEnumerable<object> msgs) => 0;
        }
    }

    public class TestProviderFactory : IProviderFactory
    {
        private readonly ILLMProvider _provider;
        public TestProviderFactory(ILLMProvider provider) { _provider = provider; }
        public bool SupportsApiRequests => true;
        public bool CanCreate(ProviderDescriptor descriptor) => descriptor.Id == "mock";
        public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider sp) => _provider;
        public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider sp) => _provider;
    }

    [Collection("AppState")]
    public class IncrementalStreamingHttpIntegrationTests : IDisposable
    {
        private readonly string _originalActiveProvider = AppState.ActiveProvider;
        private readonly string _originalActiveModel = AppState.ActiveModel;

        public void Dispose()
        {
            AppState.ActiveProvider = _originalActiveProvider;
            AppState.ActiveModel = _originalActiveModel;
        }

        private static ServiceProvider BuildServices(TestMockStreamingProvider mockProvider)
        {
            var services = new ServiceCollection();
            services.AddSingleton<Claude4NetApiServer>();
            services.AddSingleton<ProviderRegistry>(sp =>
            {
                var registry = new ProviderRegistry();
                registry.RegisterFactory(new TestProviderFactory(mockProvider));
                registry.Register(new ProviderDescriptor
                {
                    Id = "mock",
                    Label = "Mock",
                    TransportKind = "mock",
                    DefaultModels = new ProviderDefaultModels { Small = "mock-model", Large = "mock-model" },
                    Capabilities = new ProviderCapabilities { ToolCalling = true, Streaming = true }
                });
                return registry;
            });
            Claude4Net.SDK.AppState.ActiveProvider = "mock";
            Claude4Net.SDK.AppState.ActiveModel = "mock-model";
            return services.BuildServiceProvider();
        }

        private static ChatCompletionRequest CreateStreamingRequest(bool includeUsage = false, bool includeTools = false)
        {
            return new ChatCompletionRequest
            {
                Model = "mock-model",
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "test" } },
                Stream = true,
                StreamOptions = includeUsage ? new StreamOptionsDto { IncludeUsage = true } : null,
                Tools = includeTools ? new List<ToolDto> { new() { Function = new FunctionDto { Name = "native_tool" } } } : null
            };
        }

        private static async Task<List<string>> ReadSsePayloadsAsync(HttpClient client, int port, ChatCompletionRequest request)
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/chat/completions")
            {
                Content = JsonContent.Create(request)
            };
            using var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);
            var payloads = new List<string>();

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;

                string payload = line.Substring(6);
                payloads.Add(payload);
                if (payload == "[DONE]") break;
            }

            return payloads;
        }

        private static bool HasFinishReason(string payload, string expectedFinishReason)
        {
            using var document = JsonDocument.Parse(payload);
            var choices = document.RootElement.GetProperty("choices");
            return choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("finish_reason", out var finishReason) &&
                finishReason.GetString() == expectedFinishReason;
        }

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static Task StartWithTimeoutAsync(
            Claude4NetApiServer server,
            int port,
            string apiKey,
            TimeSpan requestTimeout)
        {
            return server.StartAsync(new Claude4NetApiServerOptions
            {
                Port = port,
                ApiKey = apiKey,
                RequestTimeout = requestTimeout
            });
        }

        private static async Task<(string Body, Exception? ReadError)> ReadUntilConnectionEndsAsync(
            HttpResponseMessage response)
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);
            var body = new StringBuilder();
            try
            {
                var buffer = new char[1024];
                int read;
                while ((read = await reader.ReadAsync(buffer)) > 0)
                {
                    body.Append(buffer, 0, read);
                }
                return (body.ToString(), null);
            }
            catch (Exception exception) when (exception is System.IO.IOException or HttpRequestException)
            {
                return (body.ToString(), exception);
            }
        }

        [Fact]
        public async Task R001_HttpIntegration_StreamingToolCall_EmitsToolCallsBeforeFinish_WithoutLeakage()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Claude4NetApiServer>();
            
            var mockProvider = new TestMockStreamingProvider();
            mockProvider.FragmentsToEmit = new List<string>
            {
                "Some text before... ",
                "<in", "voke name=\"test_tool\">",
                "<para", "meter name=\"arg1\">val", "ue1</pa", "rameter>",
                "</in", "voke>",
                " Text after."
            };
            
            services.AddSingleton<ProviderRegistry>(sp => {
                var reg = new ProviderRegistry();
                reg.RegisterFactory(new TestProviderFactory(mockProvider));
                reg.Register(new ProviderDescriptor { 
                    Id = "mock", 
                    Label = "Mock", 
                    TransportKind = "mock", 
                    DefaultModels = new ProviderDefaultModels { Small = "mock-model", Large = "mock-model" },
                    Capabilities = new ProviderCapabilities { ToolCalling = true, Streaming = true }
                });
                return reg;
            });
            
            Claude4Net.SDK.AppState.ActiveProvider = "mock";
            Claude4Net.SDK.AppState.ActiveModel = "mock-model";

            using var sp = services.BuildServiceProvider();
            var server = sp.GetRequiredService<Claude4NetApiServer>();

            int port = GetAvailablePort();
            string apiKey = "test-key-http-stream";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var req = new ChatCompletionRequest
                {
                    Model = "mock-model",
                    Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "test" } },
                    Stream = true,
                    Tools = new List<ToolDto> { new ToolDto { Function = new FunctionDto { Name = "test_tool" } } }
                };

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/chat/completions")
                {
                    Content = JsonContent.Create(req)
                };

                using var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                var contentSb = new StringBuilder();
                var toolEvents = new List<string>();
                bool finishReasonReceived = false;
                
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
                    string data = line.Substring(6);
                    if (data == "[DONE]") break;

                    var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, _jsonOptions);
                    var choice = chunk?.Choices?.FirstOrDefault();
                    if (choice != null)
                    {
                        if (choice.Delta?.Content != null)
                            contentSb.Append(choice.Delta.Content);
                        
                        if (choice.Delta?.ToolCalls != null && choice.Delta.ToolCalls.Count > 0)
                        {
                            toolEvents.Add(JsonSerializer.Serialize(choice.Delta.ToolCalls));
                            Assert.False(finishReasonReceived, "Tool call delta received AFTER finish reason!");
                        }

                        if (!string.IsNullOrEmpty(choice.FinishReason))
                        {
                            finishReasonReceived = true;
                        }
                    }
                }

                string finalContent = contentSb.ToString();
                Assert.DoesNotContain("<invoke", finalContent);
                Assert.DoesNotContain("</invoke>", finalContent);
                Assert.DoesNotContain("<parameter", finalContent);
                Assert.DoesNotContain("</parameter>", finalContent);
                
                Assert.Equal("Some text before...  Text after.", finalContent);
                Assert.NotEmpty(toolEvents);
                
                var argsSb = new StringBuilder();
                foreach (var lineContent in toolEvents) {
                    if (lineContent.Contains("\"arguments\":\"")) {
                        var start = lineContent.IndexOf("\"arguments\":\"") + 13;
                        var end = lineContent.IndexOf("\"}", start);
                        if (end > start) {
                            var frag = lineContent.Substring(start, end - start);
                            argsSb.Append(frag.Replace("\\u0022", "\""));
                        }
                    }
                }
                string allArgs = argsSb.ToString();
                Assert.Contains("value1", allArgs);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R002_HttpIntegration_StreamingReasoning_SeparatesCleanly_WithoutLeakage()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Claude4NetApiServer>();
            
            var mockProvider = new TestMockStreamingProvider();
            mockProvider.FragmentsToEmit = new List<string>
            {
                "Norm", "al 1 ",
                "<th", "in", "k>",
                "Reas", "oning ", "text",
                "</th", "i", "nk>",
                " Normal 2"
            };
            
            services.AddSingleton<ProviderRegistry>(sp => {
                var reg = new ProviderRegistry();
                reg.RegisterFactory(new TestProviderFactory(mockProvider));
                reg.Register(new ProviderDescriptor { 
                    Id = "mock", 
                    Label = "Mock", 
                    TransportKind = "mock", 
                    DefaultModels = new ProviderDefaultModels { Small = "mock-model", Large = "mock-model" },
                    Capabilities = new ProviderCapabilities { ToolCalling = true, Streaming = true }
                });
                return reg;
            });
            
            Claude4Net.SDK.AppState.ActiveProvider = "mock";
            Claude4Net.SDK.AppState.ActiveModel = "mock-model";

            using var sp = services.BuildServiceProvider();
            var server = sp.GetRequiredService<Claude4NetApiServer>();

            int port = GetAvailablePort();
            string apiKey = "test-key-http-stream2";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var req = new ChatCompletionRequest
                {
                    Model = "mock-model",
                    Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "test" } },
                    Stream = true
                };

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/chat/completions")
                {
                    Content = JsonContent.Create(req)
                };

                using var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new System.IO.StreamReader(stream);

                var contentSb = new StringBuilder();
                var reasoningSb = new StringBuilder();
                
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: ")) continue;
                    string data = line.Substring(6);
                    if (data == "[DONE]") break;

                    var chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(data, _jsonOptions);
                    var choice = chunk?.Choices?.FirstOrDefault();
                    if (choice != null)
                    {
                        if (choice.Delta?.Content != null)
                            contentSb.Append(choice.Delta.Content);
                        
                        if (choice.Delta?.ReasoningContent != null)
                            reasoningSb.Append(choice.Delta.ReasoningContent);
                    }
                }

                string finalContent = contentSb.ToString();
                string finalReasoning = reasoningSb.ToString();

                Assert.DoesNotContain("<think>", finalContent);
                Assert.DoesNotContain("</think>", finalContent);
                Assert.DoesNotContain("<think>", finalReasoning);
                Assert.DoesNotContain("</think>", finalReasoning);
                
                Assert.Equal("Normal 1  Normal 2", finalContent);
                Assert.Equal("Reasoning text", finalReasoning);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R003_HttpIntegration_NativeThinkingDelta_EmitsReasoningContentWithoutRawTags()
        {
            var mockProvider = new TestMockStreamingProvider
            {
                EventsToEmit = new List<LLMStreamEvent>
                {
                    new() { Type = LLMStreamEventType.ThinkingDelta, Delta = "native reasoning" },
                    new() { Type = LLMStreamEventType.TextDelta, Delta = "visible answer" },
                    new() { Type = LLMStreamEventType.Completed }
                }
            };
            using var services = BuildServices(mockProvider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "test-key-native-thinking";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var payloads = await ReadSsePayloadsAsync(client, port, CreateStreamingRequest());
                var reasoning = new StringBuilder();
                var content = new StringBuilder();

                foreach (var payload in payloads.Where(payload => payload != "[DONE]"))
                {
                    using var document = JsonDocument.Parse(payload);
                    var choices = document.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0) continue;

                    var delta = choices[0].GetProperty("delta");
                    if (delta.TryGetProperty("reasoning_content", out var reasoningContent))
                        reasoning.Append(reasoningContent.GetString());
                    if (delta.TryGetProperty("content", out var contentChunk))
                        content.Append(contentChunk.GetString());
                }

                Assert.Equal("native reasoning", reasoning.ToString());
                Assert.Equal("visible answer", content.ToString());
                Assert.DoesNotContain("<think>", string.Join("\n", payloads));
                Assert.DoesNotContain("</think>", string.Join("\n", payloads));
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R004_HttpIntegration_NativeToolCallStart_EmitsStableToolCallHeaderBeforeFinish()
        {
            var mockProvider = new TestMockStreamingProvider
            {
                EventsToEmit = new List<LLMStreamEvent>
                {
                    new()
                    {
                        Type = LLMStreamEventType.ToolCallStart,
                        ToolCall = new ToolUseRequest
                        {
                            Id = "call-native-7",
                            Name = "native_tool",
                            Input = new Dictionary<string, object> { ["path"] = "native.txt", ["depth"] = 2 }
                        }
                    },
                    new() { Type = LLMStreamEventType.Completed }
                }
            };
            using var services = BuildServices(mockProvider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "test-key-native-tool";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var payloads = await ReadSsePayloadsAsync(client, port, CreateStreamingRequest(includeTools: true));
                int toolCallHeaderIndex = payloads.FindIndex(payload =>
                {
                    if (payload == "[DONE]") return false;
                    using var document = JsonDocument.Parse(payload);
                    var choices = document.RootElement.GetProperty("choices");
                    return choices.GetArrayLength() > 0 &&
                        choices[0].GetProperty("delta").TryGetProperty("tool_calls", out _);
                });
                Assert.True(toolCallHeaderIndex >= 0, "Expected native ToolCallStart to emit a tool_calls header.");

                using var toolCallDocument = JsonDocument.Parse(payloads[toolCallHeaderIndex]);
                var toolCalls = toolCallDocument.RootElement.GetProperty("choices")[0]
                    .GetProperty("delta").GetProperty("tool_calls");
                Assert.Equal(1, toolCalls.GetArrayLength());

                var toolCall = toolCalls[0];
                Assert.Equal(0, toolCall.GetProperty("index").GetInt32());
                Assert.Equal("call-native-7", toolCall.GetProperty("id").GetString());
                Assert.Equal("native_tool", toolCall.GetProperty("function").GetProperty("name").GetString());
                string? arguments = toolCall.GetProperty("function").GetProperty("arguments").GetString();
                Assert.NotNull(arguments);
                using var argumentsDocument = JsonDocument.Parse(arguments!);
                Assert.Equal("native.txt", argumentsDocument.RootElement.GetProperty("path").GetString());
                Assert.Equal(2, argumentsDocument.RootElement.GetProperty("depth").GetInt32());

                int finishIndex = payloads.FindIndex(payload => payload != "[DONE]" && HasFinishReason(payload, "tool_calls"));
                Assert.True(finishIndex > toolCallHeaderIndex, "Expected finish_reason tool_calls after the native tool_calls header.");
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R005_HttpIntegration_SuccessfulStream_EmitsUsageAfterFinishBeforeSingleDone()
        {
            var mockProvider = new TestMockStreamingProvider
            {
                EventsToEmit = new List<LLMStreamEvent>
                {
                    new() { Type = LLMStreamEventType.TextDelta, Delta = "successful response" },
                    new() { Type = LLMStreamEventType.Completed }
                }
            };
            using var services = BuildServices(mockProvider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "test-key-successful-ordering";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var payloads = await ReadSsePayloadsAsync(client, port, CreateStreamingRequest(includeUsage: true));
                int finishIndex = payloads.FindIndex(payload => payload != "[DONE]" && HasFinishReason(payload, "stop"));
                int usageIndex = payloads.FindIndex(payload =>
                {
                    if (payload == "[DONE]") return false;
                    using var document = JsonDocument.Parse(payload);
                    return document.RootElement.GetProperty("choices").GetArrayLength() == 0 &&
                        document.RootElement.TryGetProperty("usage", out _);
                });
                int doneIndex = payloads.FindIndex(payload => payload == "[DONE]");

                Assert.True(finishIndex >= 0, "Expected a successful stream finish chunk.");
                Assert.True(usageIndex > finishIndex, "Expected the include_usage chunk after the finish chunk.");
                Assert.Equal(1, payloads.Count(payload => payload == "[DONE]"));
                Assert.True(doneIndex > usageIndex, "Expected exactly one [DONE] event after the usage chunk.");
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Wave4_TimeoutBeforeFirstSseChunk_ReturnsGatewayTimeoutEnvelope()
        {
            var provider = new TestMockStreamingProvider { BlockAfterEvents = true };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave4-timeout-before-headers";
            await StartWithTimeoutAsync(server, port, apiKey, TimeSpan.FromMilliseconds(100));

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/chat/completions")
                {
                    Content = JsonContent.Create(CreateStreamingRequest())
                };

                using HttpResponseMessage response = await client.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead);

                Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
                Assert.Contains("request_timeout", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Wave4_LegacyTimeoutBeforeFirstSseChunk_ReturnsGatewayTimeoutEnvelope()
        {
            var provider = new TestMockStreamingProvider { BlockAfterEvents = true };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave4-legacy-timeout-before-headers";
            await StartWithTimeoutAsync(server, port, apiKey, TimeSpan.FromMilliseconds(100));

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage response = await client.PostAsJsonAsync(
                    $"http://localhost:{port}/v1/completions",
                    new TextCompletionRequest
                    {
                        Model = "mock-model",
                        Prompt = "test",
                        Stream = true
                    });

                Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
                Assert.Contains("request_timeout", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Wave4_TimeoutAfterFirstChatChunk_AbortsWithoutTerminalSuffix()
        {
            var provider = new TestMockStreamingProvider
            {
                FragmentsToEmit = new List<string> { "first-chat-frame" },
                BlockAfterEvents = true
            };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave4-chat-post-header-timeout";
            await StartWithTimeoutAsync(server, port, apiKey, TimeSpan.FromMilliseconds(200));

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/chat/completions")
                {
                    Content = JsonContent.Create(CreateStreamingRequest(includeUsage: true))
                };
                using HttpResponseMessage response = await client.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead);

                (string body, Exception? readError) = await ReadUntilConnectionEndsAsync(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("first-chat-frame", body, StringComparison.Ordinal);
                Assert.NotNull(readError);
                Assert.DoesNotContain("\"finish_reason\":\"stop\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain("\"choices\":[]", body, StringComparison.Ordinal);
                Assert.DoesNotContain("\"error\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain("request_timeout", body, StringComparison.Ordinal);
                Assert.DoesNotContain("[DONE]", body, StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Wave4_TimeoutAfterFirstLegacyChunk_AbortsWithoutTerminalSuffix()
        {
            var provider = new TestMockStreamingProvider
            {
                FragmentsToEmit = new List<string> { "first-legacy-frame" },
                BlockAfterEvents = true
            };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave4-legacy-post-header-timeout";
            await StartWithTimeoutAsync(server, port, apiKey, TimeSpan.FromMilliseconds(200));

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{port}/v1/completions")
                {
                    Content = JsonContent.Create(new TextCompletionRequest
                    {
                        Model = "mock-model",
                        Prompt = "test",
                        Stream = true
                    })
                };
                using HttpResponseMessage response = await client.SendAsync(
                    requestMessage,
                    HttpCompletionOption.ResponseHeadersRead);

                (string body, Exception? readError) = await ReadUntilConnectionEndsAsync(response);

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains("first-legacy-frame", body, StringComparison.Ordinal);
                Assert.NotNull(readError);
                Assert.DoesNotContain("\"finish_reason\":\"stop\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain("\"error\"", body, StringComparison.Ordinal);
                Assert.DoesNotContain("request_timeout", body, StringComparison.Ordinal);
                Assert.DoesNotContain("[DONE]", body, StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task Wave4_ProviderEnumerator_ReceivesLinkedTokenAndIsCancelledAndDisposed()
        {
            var provider = new TestMockStreamingProvider { BlockAfterEvents = true };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave4-enumerator-cancellation";
            await StartWithTimeoutAsync(server, port, apiKey, TimeSpan.FromMilliseconds(100));

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                using HttpResponseMessage response = await client.PostAsJsonAsync(
                    $"http://localhost:{port}/v1/chat/completions",
                    CreateStreamingRequest());

                Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
                Assert.True(provider.EnumeratorToken.CanBeCanceled);
                await provider.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
                await provider.EnumeratorDisposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public async Task Wave3_DeclaredToolFilter_IsIdenticalAcrossResponsePaths(bool stream, bool nativeCalls)
        {
            var provider = new TestMockStreamingProvider();
            if (nativeCalls)
            {
                provider.EventsToEmit = new List<LLMStreamEvent>
                {
                    ToolEvent("call-hidden", "hidden_tool"),
                    ToolEvent("call-declared", "declared_tool")
                };
            }
            else
            {
                provider.FragmentsToEmit = new List<string>
                {
                    "<invoke name=\"hidden_tool\"><parameter name=\"value\">hidden</parameter></invoke>",
                    "<invoke name=\"declared_tool\"><parameter name=\"value\">visible</parameter></invoke>"
                };
            }

            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave3-tool-filter";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var request = new ChatCompletionRequest
                {
                    Model = "mock-model",
                    Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "test" } },
                    Stream = stream,
                    Tools = new List<ToolDto> { new() { Function = new FunctionDto { Name = "declared_tool" } } }
                };

                string body;
                if (stream)
                {
                    body = string.Join("\n", await ReadSsePayloadsAsync(client, port, request));
                }
                else
                {
                    using HttpResponseMessage response = await client.PostAsJsonAsync(
                        $"http://localhost:{port}/v1/chat/completions",
                        request);
                    response.EnsureSuccessStatusCode();
                    body = await response.Content.ReadAsStringAsync();
                }

                Assert.Contains("declared_tool", body, StringComparison.Ordinal);
                Assert.DoesNotContain("hidden_tool", body, StringComparison.Ordinal);
                Assert.DoesNotContain("hidden", body, StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Wave3_UndeclaredParsedToolCall_IsNeverSurfaced(bool stream)
        {
            var provider = new TestMockStreamingProvider
            {
                FragmentsToEmit = new List<string>
                {
                    "before<invoke name=\"hidden_tool\"><parameter name=\"secret\">hidden-value</parameter></invoke>after"
                }
            };
            using ServiceProvider services = BuildServices(provider);
            var server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            const string apiKey = "wave3-undeclared-parsed";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                var request = new ChatCompletionRequest
                {
                    Model = "mock-model",
                    Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "test" } },
                    Stream = stream,
                    Tools = new List<ToolDto> { new() { Function = new FunctionDto { Name = "declared_tool" } } }
                };

                string body;
                if (stream)
                {
                    body = string.Join("\n", await ReadSsePayloadsAsync(client, port, request));
                }
                else
                {
                    using HttpResponseMessage response = await client.PostAsJsonAsync(
                        $"http://localhost:{port}/v1/chat/completions",
                        request);
                    response.EnsureSuccessStatusCode();
                    body = await response.Content.ReadAsStringAsync();
                }

                Assert.DoesNotContain("hidden_tool", body, StringComparison.Ordinal);
                Assert.DoesNotContain("hidden-value", body, StringComparison.Ordinal);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        private static LLMStreamEvent ToolEvent(string id, string name) => new()
        {
            Type = LLMStreamEventType.ToolCallStart,
            ToolCall = new ToolUseRequest
            {
                Id = id,
                Name = name,
                Input = new Dictionary<string, object> { ["value"] = name }
            }
        };
    }
}
