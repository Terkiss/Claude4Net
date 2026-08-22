using Xunit;
using Claude4Net.Api;
using Claude4Net.SDK;
using Moq;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    public class K037GeminiTests
    {
        [Fact]
        public async Task GeminiProvider_StalledSseBodyRead_ObservesCancellationAfterHeaders()
        {
            string? originalApiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
            Environment.SetEnvironmentVariable("GEMINI_API_KEY", "test-key");
            var stalledStream = new StalledReadStream();
            using var client = new HttpClient(new StalledSseHandler(stalledStream));
            var mockRegistry = new Mock<IToolRegistry>();
            mockRegistry.Setup(registry => registry.GetTools()).Returns(new List<ITool>());
            var provider = new GeminiProvider(client, mockRegistry.Object);
            using var cancellation = new CancellationTokenSource();
            IAsyncEnumerator<LLMStreamEvent> enumerator = provider
                .StreamQueryAsync("hello", "gemini-2.0-flash", cancellation.Token)
                .GetAsyncEnumerator(cancellation.Token);
            Task<bool> advance = enumerator.MoveNextAsync().AsTask();

            try
            {
                await stalledStream.WaitForReadAsync(TimeSpan.FromSeconds(2));
                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => advance.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.True(stalledStream.IsDisposed);
            }
            finally
            {
                stalledStream.Release();
                try
                {
                    await advance.WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                }
                await enumerator.DisposeAsync();
                Environment.SetEnvironmentVariable("GEMINI_API_KEY", originalApiKey);
            }
        }

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

        private sealed class StalledSseHandler : HttpMessageHandler
        {
            private readonly Stream _stream;

            public StalledSseHandler(Stream stream)
            {
                _stream = stream;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(_stream)
                });
            }
        }

        private sealed class StalledReadStream : Stream
        {
            private readonly TaskCompletionSource _readStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public bool IsDisposed { get; private set; }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public async Task WaitForReadAsync(TimeSpan timeout)
                => await _readStarted.Task.WaitAsync(timeout);

            public void Release() => _release.TrySetResult();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                _readStarted.TrySetResult();
                return WaitForReleaseOrCancellationAsync(cancellationToken);
            }

            public override ValueTask<int> ReadAsync(
                Memory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                _readStarted.TrySetResult();
                return new ValueTask<int>(WaitForReleaseOrCancellationAsync(cancellationToken));
            }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }

            private async Task<int> WaitForReleaseOrCancellationAsync(CancellationToken cancellationToken)
            {
                await _release.Task.WaitAsync(cancellationToken);
                return 0;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
