using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Moq;
using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    public class K037GeminiTests
    {
        [Fact]
        public void GeminiProvider_AddMessage_StructuredToolResult_ShouldPreservePayloadAsGeminiFunctionResponse()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new GeminiProvider(new HttpClient(), mockRegistry.Object);

            var structuredContent = new { status = "Success", entries = new List<string> { "a.txt", "b/" } };

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
            var lastMessage = history.Last();
            var json = JsonSerializer.Serialize(lastMessage);

            // Should contain Gemini-style structure
            Assert.Contains("functionResponse", json);
            Assert.Contains("status", json);
            Assert.Contains("Success", json);
            Assert.Contains("entries", json);
            Assert.Contains("a.txt", json);
            Assert.Contains("b/", json);

            // Should NOT contain Anthropic-style raw 'content' field in the final stored message
            Assert.DoesNotContain("\"content\":[", json);
            Assert.DoesNotContain("System.Collections.Generic.List", json);
        }

        [Fact]
        public void GeminiProvider_AddMessage_InvalidToolResult_ShouldNotStoreRawContentMessage()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new GeminiProvider(new HttpClient(), mockRegistry.Object);

            // 1. Malformed content that might throw during processing (e.g. nested objects that we try to handle)
            // 2. We use a raw object that is NOT following the expected tool_result schema to trigger fallback
            var malformedToolResult = new
            {
                role = "user",
                content = "This is not an array, but role is user. Should be handled by fallback."
            };

            // Act
            provider.AddMessage(malformedToolResult);

            // Assert
            var history = provider.GetHistory();
            var lastMessage = history.Last();
            var json = JsonSerializer.Serialize(lastMessage);

            // Verify safe fallback structure { role, parts: [{ text: ... }] }
            Assert.DoesNotContain("\"content\":", json);
            Assert.Contains("\"parts\":", json);
            Assert.Contains("This is not an array", json);
        }

        [Fact]
        public void GeminiProvider_AddMessage_ExtremelyMalformedToolResult_ShouldTriggerCatchAndSafeFallback()
        {
            // Arrange
            var mockRegistry = new Mock<IToolRegistry>();
            var provider = new GeminiProvider(new HttpClient(), mockRegistry.Object);

            // An object that will definitely cause issues if we try to access properties that don't exist
            // OR we can pass something that is technically valid JSON but logically broken for the converter
            var crazyObject = new { role = "user", content = new[] { new { type = "tool_result", tool_use_id = (string)null!, content = new { complex = new int[] { 1, 2, 3 } } } } };

            // Act
            provider.AddMessage(crazyObject);

            // Assert
            var history = provider.GetHistory();
            var lastMessage = history.Last();
            var json = JsonSerializer.Serialize(lastMessage);

            // Even if it "succeeds" or "fails" internally, the result in history MUST NOT have a top-level 'content' field
            Assert.DoesNotContain("\"content\":[", json);
            Assert.Contains("\"parts\":", json);
        }
    }
}
