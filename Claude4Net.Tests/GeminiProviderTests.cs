using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Moq;
using Moq.Protected;
using System;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class GeminiProviderTests : IDisposable
    {
        public GeminiProviderTests()
        {
            // Setup environment variable for tests
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            AppState.ActiveModel = "gemini-2.0-flash";
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", null);
        }

        private HttpClient CreateMockClient(string sseContent)
        {
            var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
            handlerMock
               .Protected()
               .Setup<Task<HttpResponseMessage>>(
                  "SendAsync",
                  ItExpr.IsAny<HttpRequestMessage>(),
                  ItExpr.IsAny<CancellationToken>()
               )
               .ReturnsAsync(new HttpResponseMessage()
               {
                   StatusCode = HttpStatusCode.OK,
                   Content = new StringContent(sseContent)
               });

            return new HttpClient(handlerMock.Object);
        }

        [Fact]
        public async Task GeminiProvider_StreamQueryAsync_PreservesThoughtSignatureInHistory()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(r => r.GetTools()).Returns(new List<ITool>());

            // Simulation of a Gemini response with thought_signature
            var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Thinking...\"},{\"functionCall\":{\"name\":\"LsTool\",\"args\":{\"path\":\".\"}},\"thought_signature\":\"sig-123\"}]}}]}";

            var client = CreateMockClient(sse);
            var provider = new GeminiProvider(client, mockRegistry.Object);

            // Act
            await foreach (var evt in provider.StreamQueryAsync("Hi")) { }

            // Assert
            var history = provider.GetHistory();
            var modelTurn = history.FirstOrDefault(m => JsonSerializer.Serialize(m).Contains("\"role\":\"model\""));

            Assert.NotNull(modelTurn);
            var turnJson = JsonSerializer.Serialize(modelTurn);
            Assert.Contains("thought_signature", turnJson);
            Assert.Contains("sig-123", turnJson);
            Assert.Contains("functionCall", turnJson);
        }

        [Fact]
        public async Task GeminiProvider_ToolResultFlow_MapsBackToOriginalName()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(r => r.GetTools()).Returns(new List<ITool>());

            var sse = "data: {\"candidates\":[{\"content\":{\"parts\":[{\"functionCall\":{\"name\":\"TestTool\",\"args\":{}}}]}}]}";
            var client = CreateMockClient(sse);
            var provider = new GeminiProvider(client, mockRegistry.Object);

            // 1. Model turn (populates internal map)
            var events = new List<LLMStreamEvent>();
            await foreach (var evt in provider.StreamQueryAsync("Run tool")) { events.Add(evt); }

            var toolCall = events.First(e => e.Type == LLMStreamEventType.ToolCallStart).ToolCall;
            Assert.NotNull(toolCall);

            // 2. Feedback (Add tool result)
            var resultMessage = new {
                role = "user",
                content = new[] {
                    new { type = "tool_result", tool_use_id = toolCall.Id, content = "Success" }
                }
            };

            // Act
            provider.AddMessage(resultMessage);

            // Assert
            var history = provider.GetHistory();
            var lastTurnJson = JsonSerializer.Serialize(history.Last());

            Assert.Contains("\"role\":\"function\"", lastTurnJson);
            Assert.Contains("\"name\":\"TestTool\"", lastTurnJson);
            Assert.DoesNotContain(toolCall.Id, lastTurnJson); // ID should be replaced by original name
        }

        [Fact]
        public void GeminiProvider_AddMessage_ConvertsToolResultsToFunctionRole()
        {
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new GeminiProvider(new HttpClient(), mockRegistry.Object);

            var toolResult = new[]
            {
                new { type = "tool_result", tool_use_id = "test_id", content = "Result content" }
            };

            // Act
            provider.AddMessage(new { role = "user", content = toolResult });

            // Assert
            var history = provider.GetHistory();
            var lastTurnJson = JsonSerializer.Serialize(history.Last());
            Assert.Contains("\"role\":\"function\"", lastTurnJson);
            Assert.Contains("functionResponse", lastTurnJson);
        }

        [Fact]
        public void GeminiProvider_PreservesMultipleToolResultsInOneTurn()
        {
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new GeminiProvider(new HttpClient(), mockRegistry.Object);

            var toolResults = Enumerable.Range(0, 3)
                .Select(i => new { type = "tool_result", tool_use_id = $"call_{i}", content = $"Result {i}" })
                .Cast<object>()
                .ToList();

            // Act
            provider.AddMessage(new { role = "user", content = toolResults });

            // Assert
            var lastTurnJson = JsonSerializer.Serialize(provider.GetHistory().Last());
            Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(lastTurnJson, "functionResponse").Count);
            Assert.Contains("\"role\":\"function\"", lastTurnJson);
        }
    }
}
