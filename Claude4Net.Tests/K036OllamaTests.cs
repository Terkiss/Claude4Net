using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Moq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Claude4Net.Tests
{
    public class K036OllamaTests
    {
        [Fact]
        public void OllamaProvider_ContextLimit_ShouldBeRaisedByDefault()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new OllamaProvider(new HttpClient(), mockRegistry.Object);

            // Assert
            Assert.Equal(OllamaProvider.DefaultContextLimit, provider.ContextLimit);
            Assert.True(provider.ContextLimit >= 262144);
        }

        [Fact]
        public void OllamaProvider_ContextLimit_ShouldBeConfigurableViaEnv()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new OllamaProvider(new HttpClient(), mockRegistry.Object);
            var originalVal = Environment.GetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT");

            try
            {
                Environment.SetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT", "12345");
                // Act & Assert
                Assert.Equal(12345, provider.ContextLimit);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT", originalVal);
            }
        }

        [Fact]
        public void ProviderRegistry_OllamaDescriptor_ShouldSyncWithEnv()
        {
            // Arrange
            var originalVal = Environment.GetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT");
            try
            {
                Environment.SetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT", "99999");
                var registry = ProviderRegistry.CreateWithDefaults();
                var descriptor = registry.Get("ollama");

                // Assert
                Assert.NotNull(descriptor);
                Assert.Equal(99999, descriptor.ContextWindowSize);
            }
            finally
            {
                Environment.SetEnvironmentVariable("OLLAMA_CONTEXT_LIMIT", originalVal);
            }
        }

        [Fact]
        public void ProviderRegistry_OllamaDescriptor_ShouldMatchDefaultContextLimit()
        {
            // Arrange
            var registry = ProviderRegistry.CreateWithDefaults();
            var descriptor = registry.Get("ollama");

            // Assert
            Assert.NotNull(descriptor);
            Assert.Equal(OllamaProvider.GetEffectiveContextLimit(), descriptor.ContextWindowSize);
        }

        [Fact]
        public void OllamaProvider_AddMessage_ToolResultObjectContent_ShouldPreserveJson()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new OllamaProvider(new HttpClient(), mockRegistry.Object);

            var structuredContent = new { path = "C:\\test", entries = new List<string> { "file1.txt", "dir1/" } };

            var toolResult = new
            {
                role = "user",
                content = new[]
                {
                    new { type = "tool_result", tool_use_id = "call_123", content = (object)structuredContent }
                }
            };

            // Act
            provider.AddMessage(toolResult);

            // Assert
            var history = provider.GetHistory();
            var toolMessage = history.Last();
            var json = JsonSerializer.Serialize(toolMessage);

            Assert.Contains("file1.txt", json);
            Assert.Contains("dir1/", json);
            Assert.DoesNotContain("System.Collections.Generic.List", json);
        }

        [Fact]
        public async Task OllamaProvider_StreamQuery_ShouldSendNumCtxOption()
        {
            // Arrange
            var sse = "{\"message\": {\"content\": \"Done\"}, \"done\": true}";
            var handler = new CapturingOllamaHandler(sse);
            var client = new HttpClient(handler);
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(r => r.GetTools()).Returns(new List<ITool>());

            var provider = new OllamaProvider(client, mockRegistry.Object);

            // Act
            await foreach (var evt in provider.StreamQueryAsync("Test")) { }

            // Assert
            Assert.Equal(1, handler.SendCount);
            Assert.NotNull(handler.CapturedPayload);
            Assert.Contains("\"num_ctx\":" + OllamaProvider.GetEffectiveContextLimit(), handler.CapturedPayload);
        }

        private sealed class CapturingOllamaHandler : HttpMessageHandler
        {
            private readonly string _sse;

            public CapturingOllamaHandler(string sse)
            {
                _sse = sse;
            }

            public int SendCount { get; private set; }
            public string? CapturedPayload { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SendCount++;
                CapturedPayload = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage()
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent(_sse)
                };
            }
        }
    }

    [Collection("AppState")]
    public class K036AgentLoopTests
    {
        [Fact]
        public async Task AgentLoop_ToolResultPayload_ShouldPreserveStructuredContent()
        {
            // Arrange
            var approvalHandler = new Mock<IUserApprovalHandler>().Object;
            var orchestratorServices = new ServiceCollection().BuildServiceProvider();
            var mockOrchestrator = new Mock<ToolOrchestrator>(new List<ITool>(), approvalHandler, orchestratorServices);
            var mockBroker = new Mock<IInputBroker>();
            var mockRouter = new Mock<ISmartRouter>();
            var mockProvider = new Mock<ILLMProvider>();
            var mockOutput = new Mock<IOutputHandler>();

            var services = new ServiceCollection();
            services.AddSingleton(mockOrchestrator.Object);
            services.AddSingleton(mockBroker.Object);
            services.AddSingleton(mockRouter.Object);
            services.AddSingleton(mockProvider.Object);
            var serviceProvider = services.BuildServiceProvider();

            var loop = new AgentLoop(mockOrchestrator.Object, serviceProvider, mockBroker.Object, mockRouter.Object);

            // Setup structured result from tool
            var structuredContent = new { Status = "Success", Files = new List<string> { "a.txt" } };
            var toolResult = new ToolUseResult { ToolUseId = "call_1", Content = structuredContent, IsError = false };

            mockOrchestrator
                .Setup(o => o.ExecuteBatchAsync(It.IsAny<IEnumerable<ToolUseRequest>>(), It.IsAny<object>(), It.IsAny<IUserApprovalHandler>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ToolUseResult> { toolResult });

            mockProvider.Setup(p => p.Name).Returns("test-provider");
            mockProvider.Setup(p => p.TokenCounter).Returns(new DefaultTokenCounter());
            mockProvider.Setup(p => p.ContextLimit).Returns(100000);
            mockProvider.Setup(p => p.GetHistory()).Returns(new List<object>());

            var toolCall = new ToolUseRequest { Id = "call_1", Name = "test_tool", Input = new { } };
            var response1 = new LLMResponse { ToolCalls = new List<ToolUseRequest> { toolCall } };
            var response2 = new LLMResponse { Text = "Finished" };

            mockProvider
                .SetupSequence(p => p.StreamQueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new[] { new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = response1 } }.ToAsyncEnumerable())
                .Returns(new[] { new LLMStreamEvent { Type = LLMStreamEventType.Completed, FinalResponse = response2 } }.ToAsyncEnumerable());

            AppState.CurrentCwd = System.IO.Directory.GetCurrentDirectory();
            AppState.SessionId = "test-session";

            // Act
            await loop.RunAsync("start", mockOutput.Object, mockProvider.Object, "test-model");

            // Assert
            mockProvider.Verify(p => p.AddMessage(It.Is<object>(m => HasStructuredContent(m))), Times.Once);
        }

        private static bool HasStructuredContent(object m)
        {
            var json = JsonSerializer.Serialize(m);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("content", out var content)) return false;
            if (content.ValueKind != JsonValueKind.Array) return false;

            var firstItem = content.EnumerateArray().FirstOrDefault();
            if (firstItem.ValueKind != JsonValueKind.Object) return false;

            if (!firstItem.TryGetProperty("type", out var typeProp) || typeProp.GetString() != "tool_result") return false;

            var innerContent = firstItem.GetProperty("content");
            // If it was stringified, it would be ValueKind.String.
            // But we want it to be ValueKind.Object (the original structured data).
            return innerContent.ValueKind == JsonValueKind.Object &&
                   innerContent.TryGetProperty("Files", out var filesProp) &&
                   filesProp.ValueKind == JsonValueKind.Array;
        }
    }

    public class K036ToolResultTests
    {
        [Fact]
        public void LsTool_ResultSerialization_ShouldExposeEntries()
        {
            // Verify it doesn't collapse to "System.Collections.Generic.List"
            // when serialized via JsonSerializer (as used in OllamaProvider.AddMessage).

            var entries = new List<string> { "a.txt", "b/" };
            var result = new { path = "root", entries = entries };

            // This is what AgentLoop used to do (BAD)
            string collapsed = result.ToString()!;
            Assert.Contains("System.Collections.Generic.List", collapsed);

            // This is what we want now: structured serialization
            string json = JsonSerializer.Serialize(result);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("entries", out var entriesProp));
            Assert.Equal(JsonValueKind.Array, entriesProp.ValueKind);
            Assert.Equal("a.txt", entriesProp.EnumerateArray().First().GetString());
        }
    }

    public static class AsyncEnumerableExtensions
    {
        public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
            {
                await Task.Yield();
                yield return item;
            }
        }
    }
}
