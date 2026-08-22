using System;
using System.ClientModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using OpenAI.Models;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class OfficialOpenAiDotNetSdkBlackBoxTests : IAsyncLifetime
    {
        private ServiceProvider _serviceProvider = null!;
        private Claude4NetApiServer _server = null!;
        private int _testPort;
        private const string TestApiKey = "c4n-sk-dotnet-sdk-key-8888";

        private string? _origCwd;
        private string _origSessionId = null!;
        private string _origActiveProvider = null!;
        private string _origActiveModel = null!;
        private PermissionMode _origPermissionMode;
        private bool _origIsExplicit;

        private OpenAIClient _sdkClient = null!;

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async Task InitializeAsync()
        {
            _origCwd = AppState.CurrentCwd;
            _origSessionId = AppState.SessionId;
            _origActiveProvider = AppState.ActiveProvider;
            _origActiveModel = AppState.ActiveModel;
            _origPermissionMode = AppState.CurrentPermissionMode;
            _origIsExplicit = AppState.IsProviderExplicitlySet;

            var services = new ServiceCollection();
            services.AddSingleton<IProviderFactory, DotNetSdkMockProviderFactory>();
            CliServiceRegistration.ConfigureServices(services);
            services.AddSingleton(Wave2TestSupport.CreateOfficialSdkRegistry("mock"));
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
            services.AddSingleton<Claude4NetApiServer>();

            _serviceProvider = services.BuildServiceProvider();
            _server = _serviceProvider.GetRequiredService<Claude4NetApiServer>();

            _testPort = GetAvailablePort();
            await _server.StartAsync(_testPort, TestApiKey);

            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{_testPort}/v1")
            };
            _sdkClient = new OpenAIClient(new ApiKeyCredential(TestApiKey), options);
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
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
        public async Task DotNetSdk_GetModels_ReturnsRegisteredModels()
        {
            var modelClient = _sdkClient.GetOpenAIModelClient();
            var response = await modelClient.GetModelsAsync();
            var models = response.Value.ToList();

            Assert.NotEmpty(models);
            Assert.Contains(models, m => m.Id == "claude-3-5-sonnet-20241022" || m.Id == "gpt-4o");
        }

        [Fact]
        public async Task DotNetSdk_GetModel_Valid_ReturnsModelCard()
        {
            var modelClient = _sdkClient.GetOpenAIModelClient();
            var model = await modelClient.GetModelAsync("claude-3-5-sonnet-20241022");

            Assert.NotNull(model.Value);
            Assert.Equal("claude-3-5-sonnet-20241022", model.Value.Id);
            Assert.Equal("anthropic", model.Value.OwnedBy);
        }

        [Fact]
        public async Task DotNetSdk_GetModel_Invalid_ThrowsClientResultException404()
        {
            var modelClient = _sdkClient.GetOpenAIModelClient();
            var ex = await Assert.ThrowsAsync<ClientResultException>(async () =>
            {
                await modelClient.GetModelAsync("non-existent-model-xyz-9999");
            });

            Assert.Equal(404, ex.Status);
        }

        [Fact]
        public async Task DotNetSdk_ChatCompletion_NonStreaming_ReturnsAssistantMessage()
        {
            var chatClient = _sdkClient.GetChatClient("claude-3-5-sonnet");
            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateUserMessage("Hello official .NET SDK")
            };

            var completion = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
            {
                MaxOutputTokenCount = 100
            });

            Assert.NotNull(completion.Value);
            Assert.StartsWith("chatcmpl-", completion.Value.Id);
            Assert.Equal(ChatFinishReason.Stop, completion.Value.FinishReason);
            Assert.NotEmpty(completion.Value.Content);
            Assert.NotNull(completion.Value.Usage);
            Assert.True(completion.Value.Usage.TotalTokenCount > 0);
        }

        [Fact]
        public async Task DotNetSdk_ChatCompletion_Streaming_EmitsTokensAndCompletes()
        {
            var chatClient = _sdkClient.GetChatClient("claude-3-5-sonnet");
            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateUserMessage("Stream this test message")
            };

            var updates = chatClient.CompleteChatStreamingAsync(messages);
            var contentPieces = new List<string>();

            await foreach (var update in updates)
            {
                foreach (var part in update.ContentUpdate)
                {
                    if (!string.IsNullOrEmpty(part.Text))
                    {
                        contentPieces.Add(part.Text);
                    }
                }
            }

            string fullContent = string.Join("", contentPieces);
            Assert.NotEmpty(fullContent);
            Assert.Contains("Mock dotnet-sdk response", fullContent);
        }

        [Fact]
        public async Task DotNetSdk_ChatCompletion_StreamingToolCalls_ReassemblesArgumentsCorrectly()
        {
            var chatClient = _sdkClient.GetChatClient("claude-3-5-sonnet");
            var tool = ChatTool.CreateFunctionTool(
                functionName: "calculator",
                functionDescription: "Calculates numbers",
                functionParameters: BinaryData.FromString("{\"type\":\"object\",\"properties\":{\"number\":{\"type\":\"string\"}},\"required\":[\"number\"]}")
            );

            var options = new ChatCompletionOptions();
            options.Tools.Add(tool);

            var messages = new List<ChatMessage>
            {
                ChatMessage.CreateUserMessage("invoke tool calculator with number 42")
            };

            var updates = chatClient.CompleteChatStreamingAsync(messages, options);

            string functionName = "";
            string functionArgs = "";
            string toolCallId = "";
            ChatFinishReason? finishReason = null;

            await foreach (var update in updates)
            {
                if (update.FinishReason.HasValue)
                {
                    finishReason = update.FinishReason.Value;
                }

                foreach (var tcUpdate in update.ToolCallUpdates)
                {
                    if (!string.IsNullOrEmpty(tcUpdate.ToolCallId))
                    {
                        toolCallId = tcUpdate.ToolCallId;
                    }
                    if (!string.IsNullOrEmpty(tcUpdate.FunctionName))
                    {
                        functionName += tcUpdate.FunctionName;
                    }
                    if (tcUpdate.FunctionArgumentsUpdate != null)
                    {
                        functionArgs += tcUpdate.FunctionArgumentsUpdate.ToString();
                    }
                }
            }

            Assert.Equal(ChatFinishReason.ToolCalls, finishReason);
            Assert.Equal("calculator", functionName);
            Assert.StartsWith("call_", toolCallId);
            Assert.Contains("42", functionArgs);

            using var doc = JsonDocument.Parse(functionArgs);
            Assert.Equal("42", doc.RootElement.GetProperty("number").GetString());
        }

        [Fact]
        public async Task DotNetSdk_Embeddings_GeneratesValidFloatVector()
        {
            var embClient = _sdkClient.GetEmbeddingClient("text-embedding-004");
            var result = await embClient.GenerateEmbeddingAsync("Embed this text via official .NET SDK");

            Assert.NotNull(result.Value);
            var vector = result.Value.ToFloats();
            Assert.Equal(768, vector.Length);
        }

        [Fact]
        public async Task DotNetSdkRunner_LegacyCompletions_RawHttpClient_ReturnsCompatibleCompletion()
        {
            const string model = "claude-3-5-sonnet";
            // OpenAI .NET 2.13 has no legacy Completions client.
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_testPort}/v1/") };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestApiKey);

            using var response = await client.PostAsJsonAsync("completions", new
            {
                model,
                prompt = "Complete this official .NET SDK compatibility probe",
                max_tokens = 50
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;
            JsonElement choice = root.GetProperty("choices")[0];
            JsonElement usage = root.GetProperty("usage");

            Assert.StartsWith("cmpl-", root.GetProperty("id").GetString());
            Assert.Equal(model, root.GetProperty("model").GetString());
            Assert.False(string.IsNullOrWhiteSpace(choice.GetProperty("text").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(choice.GetProperty("finish_reason").GetString()));
            Assert.True(usage.GetProperty("total_tokens").GetInt32() > 0);
        }

        [Fact]
        public async Task DotNetSdk_AuthError_ThrowsClientResultException401()
        {
            var badOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri($"http://127.0.0.1:{_testPort}/v1")
            };
            var badClient = new OpenAIClient(new ApiKeyCredential("invalid-key-xyz"), badOptions);
            var modelClient = badClient.GetOpenAIModelClient();

            var ex = await Assert.ThrowsAsync<ClientResultException>(async () =>
            {
                await modelClient.GetModelsAsync();
            });

            Assert.Equal(401, ex.Status);
        }

        private class DotNetSdkMockProviderFactory : IProviderFactory
        {
            public string TransportKind => "mock";
            public bool SupportsApiRequests => true;
            public bool CanCreate(ProviderDescriptor descriptor) => true;
            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new DotNetSdkMockProvider();
            }

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new DotNetSdkMockProvider();
            }
        }

        private class DotNetSdkMockProvider : ILLMProvider
        {
            private readonly List<object> _history = new();
            public string Name => "DotNetSdkMockProvider";
            public ITokenCounter TokenCounter { get; } = new DotNetSdkTokenCounter();
            public int ContextLimit => 200000;

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(string prompt, string? model = null, [EnumeratorCancellation] CancellationToken ct = default)
            {
                await Task.Yield();
                if (prompt.Contains("invoke tool", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<invoke name=\"calculator\"><parameter name=\"number\">42</parameter></invoke>" };
                }
                else if (prompt.Contains("reasoning test", StringComparison.OrdinalIgnoreCase))
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "<think>Step 1: thinking</think>Final response." };
                }
                else
                {
                    yield return new LLMStreamEvent { Type = LLMStreamEventType.TextDelta, Delta = "Mock dotnet-sdk response for: " + prompt };
                }
            }

            public void AddMessage(object message) { if (message != null) _history.Add(message); }
            public IReadOnlyList<object> GetHistory() => _history;
            public void SetHistory(IEnumerable<object> history) { _history.Clear(); if (history != null) _history.AddRange(history); }
            public void ClearHistory() => _history.Clear();
        }

        private class DotNetSdkTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => Math.Max(1, (text?.Length ?? 0) / 4);
            public int CountTokens(object message) => 10;
            public int CountTokens(IEnumerable<object> history) => 50;
        }
    }
}
