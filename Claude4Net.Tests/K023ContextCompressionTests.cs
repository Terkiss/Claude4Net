using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;
using Claude4Net.SDK;

namespace Claude4Net.Tests
{
    public class K023ContextCompressionTests
    {
        [Fact]
        public void DefaultTokenCounter_ShouldCountTokensHeuristically()
        {
            var counter = new DefaultTokenCounter();
            string text = "Hello world"; // 11 chars
            int tokens = counter.CountTokens(text);

            // (11 / 2) + 2 = 5 + 2 = 7
            Assert.Equal(7, tokens);
        }

        [Fact]
        public void ContextCompressor_ShouldKeepToolCallsAndTail()
        {
            var counter = new DefaultTokenCounter();
            var history = new List<object>();

            // Fill history to exceed limit
            for (int i = 0; i < 10; i++)
            {
                history.Add(new { role = "user", content = $"Message {i} that is somewhat long to increase token count." });
            }

            // Add a tool call and result in the middle (should be preserved)
            string toolId = "tool_123";
            var toolCall = new {
                role = "assistant",
                content = new[] { new { type = "tool_use", id = toolId, name = "ls", input = new { path = "." } } }
            };
            var toolResult = new {
                role = "user",
                content = new[] { new { type = "tool_result", tool_use_id = toolId, content = "file1.txt, file2.txt" } }
            };

            history.Insert(5, toolCall);
            history.Insert(6, toolResult);

            // Add 5 more messages (tail)
            for (int i = 10; i < 15; i++)
            {
                history.Add(new { role = "user", content = $"Tail message {i}" });
            }

            int currentTokens = counter.CountTokens(history);
            int limit = currentTokens / 2; // Force compression

            var compressed = ContextCompressor.Compress(history, counter, limit);

            // Verify tail is preserved (last 5)
            var lastFive = compressed.TakeLast(5).Cast<dynamic>().ToList();
            Assert.Contains("Tail message 14", (string)JsonSerializer.Serialize(lastFive.Last()));

            // Verify tool call pair is preserved
            string compressedJson = JsonSerializer.Serialize(compressed);
            Assert.Contains(toolId, compressedJson);
            Assert.Contains("tool_use", compressedJson);
            Assert.Contains("tool_result", compressedJson);

            // Verify token count is reduced
            Assert.True(counter.CountTokens(compressed) < currentTokens);
        }

        [Fact]
        public void ContextCompressor_GeminiStyle_ShouldPreserveToolCalls()
        {
            var counter = new DefaultTokenCounter();
            var history = new List<object>();

            for (int i = 0; i < 10; i++)
                history.Add(new { role = "user", parts = new[] { new { text = $"Msg {i}" } } });

            string funcName = "get_weather";
            var toolCall = new { role = "model", parts = new[] { new { functionCall = new { name = funcName, args = new { location = "Seoul" } } } } };
            var toolRes = new { role = "function", parts = new[] { new { functionResponse = new { name = funcName, response = new { temp = 25 } } } } };

            history.Insert(5, toolCall);
            history.Insert(6, toolRes);

            for (int i = 10; i < 15; i++)
                history.Add(new { role = "user", parts = new[] { new { text = $"Tail {i}" } } });

            int currentTokens = counter.CountTokens(history);
            int limit = currentTokens / 2;

            var compressed = ContextCompressor.Compress(history, counter, limit);

            string compressedJson = JsonSerializer.Serialize(compressed);
            Assert.Contains(funcName, compressedJson);
            Assert.Contains("functionCall", compressedJson);
            Assert.Contains("functionResponse", compressedJson);
        }

        [Fact]
        public void SummarizeToolResults_ShouldCollapseMultipleResults()
        {
            var toolResults = new List<object>
            {
                new { type = "tool_result", tool_use_id = "1", content = "res1" },
                new { type = "tool_result", tool_use_id = "2", content = "res2" },
                new { type = "tool_result", tool_use_id = "3", content = "res3" },
                new { type = "tool_result", tool_use_id = "4", content = "res4" }
            };

            var summarized = ContextCompressor.SummarizeToolResults(toolResults);

            Assert.Single(summarized);
            string json = JsonSerializer.Serialize(summarized[0]);
            Assert.Contains("Collapsed 4 tool results", json);
        }
    }
}
