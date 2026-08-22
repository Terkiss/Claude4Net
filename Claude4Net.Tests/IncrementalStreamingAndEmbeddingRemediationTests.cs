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
using Claude4Net.Commands;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.Runtime.ApiServer.Streaming;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Claude4Net.Tests
{
    public class TestMockEmbeddingProvider : IEmbeddingProvider
    {
        public const int NativeDimensions = 768;
        public string ProviderId => "gemini";
        public string ModelId => "text-embedding-004";

        public Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct = default)
        {
            // Deterministic real vector simulation with constant length 768
            var vector = new float[NativeDimensions];
            for (int i = 0; i < NativeDimensions; i++)
            {
                vector[i] = (float)Math.Sin(i + text.Length) / (float)Math.Sqrt(NativeDimensions);
            }
            return Task.FromResult(vector);
        }
    }

    [Collection("AppState")]
    public class IncrementalStreamingAndEmbeddingRemediationTests
    {
        // -----------------------------------------------------------------------------------------
        // R-002: Stream-Safe Incremental Reasoning Parser Unit & Boundary Tests
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void R002_ReasoningParser_OpeningTagSplitAtEverySingleCharacter_SuppressesTagAndExtractsReasoning()
        {
            var parser = new IncrementalReasoningParser();
            var chunks = new[] { "<", "t", "h", "i", "n", "k", ">", "Step 1: thinking", "</", "t", "h", "i", "n", "k", ">", "Final answer" };

            var reasoningSb = new StringBuilder();
            var contentSb = new StringBuilder();

            foreach (var chunk in chunks)
            {
                foreach (var parsed in parser.ProcessChunk(chunk))
                {
                    if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                    else contentSb.Append(parsed.Text);
                }
            }

            foreach (var parsed in parser.Flush())
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }

            Assert.Equal("Step 1: thinking", reasoningSb.ToString());
            Assert.Equal("Final answer", contentSb.ToString());
            Assert.DoesNotContain("<think>", contentSb.ToString());
            Assert.DoesNotContain("</think>", contentSb.ToString());
            Assert.DoesNotContain("<think>", reasoningSb.ToString());
            Assert.DoesNotContain("</think>", reasoningSb.ToString());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(10)]
        [InlineData(50)]
        public void R002_ReasoningParser_ArbitraryChunkSizes_ProducesIdenticalExtractedReasoningAndContent(int chunkSize)
        {
            string fullRaw = "Introduction before reasoning. <think>This is deep internal chain of thought step 1. Step 2.</think> The real final user response.";

            var parser = new IncrementalReasoningParser();
            var reasoningSb = new StringBuilder();
            var contentSb = new StringBuilder();

            for (int i = 0; i < fullRaw.Length; i += chunkSize)
            {
                string slice = fullRaw.Substring(i, Math.Min(chunkSize, fullRaw.Length - i));
                foreach (var parsed in parser.ProcessChunk(slice))
                {
                    if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                    else contentSb.Append(parsed.Text);
                }
            }

            foreach (var parsed in parser.Flush())
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }

            Assert.Equal("This is deep internal chain of thought step 1. Step 2.", reasoningSb.ToString());
            Assert.Equal("Introduction before reasoning.  The real final user response.", contentSb.ToString());
        }

        [Fact]
        public void R002_ReasoningParser_NonReasoningAngleBrackets_PreservedAsNormalContent()
        {
            var parser = new IncrementalReasoningParser();
            string text = "5 < 10 and 20 > 5 and <thi not a think tag and <thin not quite either.";

            var contentSb = new StringBuilder();
            var reasoningSb = new StringBuilder();

            foreach (var parsed in parser.ProcessChunk(text))
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }
            foreach (var parsed in parser.Flush())
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }

            Assert.Empty(reasoningSb.ToString());
            Assert.Equal(text, contentSb.ToString());
        }

        [Fact]
        public void R002_ReasoningParser_UnclosedThinkTag_FlushExtractsRemainingAsReasoning()
        {
            var parser = new IncrementalReasoningParser();
            string text = "<think>Incomplete reasoning that never closed";

            var reasoningSb = new StringBuilder();
            var contentSb = new StringBuilder();

            foreach (var parsed in parser.ProcessChunk(text))
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }
            foreach (var parsed in parser.Flush())
            {
                if (parsed.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(parsed.Text);
                else contentSb.Append(parsed.Text);
            }

            Assert.Equal("Incomplete reasoning that never closed", reasoningSb.ToString());
            Assert.Empty(contentSb.ToString());
        }

        [Fact]
        public void R002_ReasoningParser_EmojiSurrogatePairIntact_NeverCorruptedByJsonSerialization()
        {
            var parser = new IncrementalReasoningParser();
            string incoming = "Hello world 👋 <think>Thinking with emoji 🚀 and 한국어 ₩1,000</think> Response with 🎉 sparkle!";

            var chunks = new List<string>();
            foreach (var parsed in parser.ProcessChunk(incoming))
            {
                var dto = new ChatCompletionChunk
                {
                    Choices = new List<ChatChunkChoiceDto>
                    {
                        new()
                        {
                            Delta = new ChatChunkDeltaDto
                            {
                                Content = parsed.Kind == ReasoningChunkKind.Content ? parsed.Text : null,
                                ReasoningContent = parsed.Kind == ReasoningChunkKind.Reasoning ? parsed.Text : null
                            }
                        }
                    }
                };
                string json = JsonSerializer.Serialize(dto);
                Assert.DoesNotContain("\uFFFD", json); // Verify NO unicode replacement character!
                chunks.Add(json);
            }

            foreach (var parsed in parser.Flush())
            {
                var dto = new ChatCompletionChunk
                {
                    Choices = new List<ChatChunkChoiceDto>
                    {
                        new()
                        {
                            Delta = new ChatChunkDeltaDto
                            {
                                Content = parsed.Kind == ReasoningChunkKind.Content ? parsed.Text : null,
                                ReasoningContent = parsed.Kind == ReasoningChunkKind.Reasoning ? parsed.Text : null
                            }
                        }
                    }
                };
                string json = JsonSerializer.Serialize(dto);
                Assert.DoesNotContain("\uFFFD", json);
                chunks.Add(json);
            }

            Assert.NotEmpty(chunks);
            // Verify chunks are batched efficiently, not 1-char per chunk
            Assert.True(chunks.Count <= 5, $"Expected <= 5 batched chunks but got {chunks.Count}");
        }

        // -----------------------------------------------------------------------------------------
        // R-001: Stream-Safe Incremental Tool Call Parser Unit & Boundary Tests
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void R001_ToolCallParser_SingleInvoke_IncrementalChunkSplits_EmitsHeaderThenProgressiveArguments()
        {
            var parser = new IncrementalToolCallParser();
            var chunks = new[]
            {
                "Intro text\n",
                "<in", "voke name=\"calculate_tax\">",
                "<param", "eter name=\"amount\">",
                "10", "00",
                "</param", "eter>",
                "<parameter name=\"rate\">0.15</parameter>",
                "</in", "voke>",
                "\nOutro text"
            };

            var contentSb = new StringBuilder();
            var toolEvents = new List<ToolParsedEvent>();

            foreach (var chunk in chunks)
            {
                foreach (var ev in parser.ProcessChunk(chunk))
                {
                    if (ev.Type == ToolParsedEventType.ContentDelta) contentSb.Append(ev.Content);
                    else toolEvents.Add(ev);
                }
            }

            foreach (var ev in parser.Flush())
            {
                if (ev.Type == ToolParsedEventType.ContentDelta) contentSb.Append(ev.Content);
                else toolEvents.Add(ev);
            }

            Assert.Equal("Intro text\n\nOutro text", contentSb.ToString());
            Assert.DoesNotContain("<invoke", contentSb.ToString());
            Assert.DoesNotContain("</invoke>", contentSb.ToString());
            Assert.DoesNotContain("<parameter", contentSb.ToString());
            Assert.DoesNotContain("</parameter>", contentSb.ToString());

            // Check Tool Header
            var header = toolEvents.FirstOrDefault(e => e.Type == ToolParsedEventType.ToolCallHeader);
            Assert.NotNull(header);
            Assert.Equal(0, header.ToolIndex);
            Assert.Equal("calculate_tax", header.ToolName);
            Assert.StartsWith("call_", header.ToolId!);

            // Check Reassembled Arguments JSON
            var argDeltas = toolEvents.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta).Select(e => e.ArgumentDelta);
            string fullArgs = string.Join("", argDeltas);

            using var doc = JsonDocument.Parse(fullArgs);
            var root = doc.RootElement;
            Assert.Equal("1000", root.GetProperty("amount").GetString());
            Assert.Equal("0.15", root.GetProperty("rate").GetString());
        }

        [Fact]
        public void R001_ToolCallParser_MultipleSequentialToolsInSingleStream_PreservesSequentialIndicesAndDistinctIds()
        {
            var parser = new IncrementalToolCallParser();
            string raw = "<invoke name=\"get_weather\"><parameter name=\"city\">Seoul</parameter></invoke>" +
                         "<invoke name=\"get_currency\"><parameter name=\"pair\">USD/KRW</parameter></invoke>";

            var events = new List<ToolParsedEvent>();
            foreach (char c in raw)
            {
                events.AddRange(parser.ProcessChunk(c.ToString()));
            }
            events.AddRange(parser.Flush());

            var headers = events.Where(e => e.Type == ToolParsedEventType.ToolCallHeader).ToList();
            Assert.Equal(2, headers.Count);

            Assert.Equal(0, headers[0].ToolIndex);
            Assert.Equal("get_weather", headers[0].ToolName);

            Assert.Equal(1, headers[1].ToolIndex);
            Assert.Equal("get_currency", headers[1].ToolName);

            Assert.NotEqual(headers[0].ToolId, headers[1].ToolId);

            // Reconstruct tool 0 arguments
            string tool0Args = string.Join("", events.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta && e.ToolIndex == 0).Select(e => e.ArgumentDelta));
            using var doc0 = JsonDocument.Parse(tool0Args);
            Assert.Equal("Seoul", doc0.RootElement.GetProperty("city").GetString());

            // Reconstruct tool 1 arguments
            string tool1Args = string.Join("", events.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta && e.ToolIndex == 1).Select(e => e.ArgumentDelta));
            using var doc1 = JsonDocument.Parse(tool1Args);
            Assert.Equal("USD/KRW", doc1.RootElement.GetProperty("pair").GetString());
        }

        [Fact]
        public void R001_ToolCallParser_UnicodeAndSpecialCharacters_ReassemblesProperJson()
        {
            var parser = new IncrementalToolCallParser();
            string raw = "<invoke name=\"send_message\"><parameter name=\"text\">서울 날씨: 맑음 ☀️ \"최고 기온\" ₩1,000</parameter></invoke>";

            var events = new List<ToolParsedEvent>();
            foreach (char c in raw)
            {
                events.AddRange(parser.ProcessChunk(c.ToString()));
            }
            events.AddRange(parser.Flush());

            string fullArgs = string.Join("", events.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta).Select(e => e.ArgumentDelta));
            using var doc = JsonDocument.Parse(fullArgs);
            Assert.Equal("서울 날씨: 맑음 ☀️ \"최고 기온\" ₩1,000", doc.RootElement.GetProperty("text").GetString());
        }

        [Fact]
        public void R001_ToolCallParser_EmptyParameters_EmitsEmptyJsonObject()
        {
            var parser = new IncrementalToolCallParser();
            string raw = "<invoke name=\"get_current_time\"></invoke>";

            var events = new List<ToolParsedEvent>();
            events.AddRange(parser.ProcessChunk(raw));
            events.AddRange(parser.Flush());

            var header = events.FirstOrDefault(e => e.Type == ToolParsedEventType.ToolCallHeader);
            Assert.NotNull(header);
            Assert.Equal("get_current_time", header.ToolName);

            string fullArgs = string.Join("", events.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta).Select(e => e.ArgumentDelta));
            Assert.Equal("{}", fullArgs);
        }

        [Fact]
        public void R001_ToolCallParser_UnclosedInvoke_FlushClosesGracefullyWithoutLeakingOrThrowing()
        {
            var parser = new IncrementalToolCallParser();
            string raw = "<invoke name=\"unfinished\"><parameter name=\"arg\">partial val";

            var events = new List<ToolParsedEvent>();
            events.AddRange(parser.ProcessChunk(raw));
            events.AddRange(parser.Flush());

            string fullArgs = string.Join("", events.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta).Select(e => e.ArgumentDelta));
            using var doc = JsonDocument.Parse(fullArgs);
            Assert.Equal("partial val", doc.RootElement.GetProperty("arg").GetString());
        }

        [Fact]
        public void R001_ToolCallParser_EmojiSurrogatePairIntact_NeverCorruptedByJsonSerialization()
        {
            var parser = new IncrementalToolCallParser();
            string raw = "Prefix text with 👋 emoji <invoke name=\"chat\"><parameter name=\"msg\">Hello from 서울 🚀 \"안녕\" ₩50,000 ✨</parameter></invoke> Postfix 🎉";

            var chunks = new List<string>();
            foreach (var ev in parser.ProcessChunk(raw))
            {
                var dto = new ChatCompletionChunk
                {
                    Choices = new List<ChatChunkChoiceDto>
                    {
                        new()
                        {
                            Delta = new ChatChunkDeltaDto
                            {
                                Content = ev.Type == ToolParsedEventType.ContentDelta ? ev.Content : null,
                                ToolCalls = ev.Type == ToolParsedEventType.ToolCallHeader
                                    ? new List<ToolCallDto> { new() { Index = ev.ToolIndex, Id = ev.ToolId, Function = new FunctionCallDto { Name = ev.ToolName ?? "", Arguments = "" } } }
                                    : ev.Type == ToolParsedEventType.ToolCallArgumentDelta
                                    ? new List<ToolCallDto> { new() { Index = ev.ToolIndex, Function = new FunctionCallDto { Arguments = ev.ArgumentDelta ?? "" } } }
                                    : null
                            }
                        }
                    }
                };
                string json = JsonSerializer.Serialize(dto);
                Assert.DoesNotContain("\uFFFD", json); // Verify NO replacement character!
                chunks.Add(json);
            }

            foreach (var ev in parser.Flush())
            {
                var dto = new ChatCompletionChunk
                {
                    Choices = new List<ChatChunkChoiceDto>
                    {
                        new()
                        {
                            Delta = new ChatChunkDeltaDto
                            {
                                Content = ev.Type == ToolParsedEventType.ContentDelta ? ev.Content : null,
                                ToolCalls = ev.Type == ToolParsedEventType.ToolCallArgumentDelta
                                    ? new List<ToolCallDto> { new() { Index = ev.ToolIndex, Function = new FunctionCallDto { Arguments = ev.ArgumentDelta ?? "" } } }
                                    : null
                            }
                        }
                    }
                };
                string json = JsonSerializer.Serialize(dto);
                Assert.DoesNotContain("\uFFFD", json);
                chunks.Add(json);
            }

            Assert.NotEmpty(chunks);
            // Verify chunks are batched efficiently, not 1-char per chunk
            Assert.True(chunks.Count <= 8, $"Expected <= 8 batched chunks but got {chunks.Count}");
        }

        // -----------------------------------------------------------------------------------------
        // Combined Pipeline Test: Reasoning + Tool Calls in Stream
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void CombinedPipeline_ReasoningFollowedByToolCall_FiltersBothCleanly()
        {
            var reasoningParser = new IncrementalReasoningParser();
            var toolParser = new IncrementalToolCallParser();

            string rawStream = "I am thinking... <think>User wants the weather in Tokyo.</think> " +
                               "I will now fetch the data. <invoke name=\"get_weather\"><parameter name=\"city\">Tokyo</parameter></invoke> Processing complete.";

            var reasoningSb = new StringBuilder();
            var contentSb = new StringBuilder();
            var toolEvents = new List<ToolParsedEvent>();

            foreach (char c in rawStream)
            {
                foreach (var rChunk in reasoningParser.ProcessChunk(c.ToString()))
                {
                    if (rChunk.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(rChunk.Text);
                    else
                    {
                        foreach (var tEvent in toolParser.ProcessChunk(rChunk.Text))
                        {
                            if (tEvent.Type == ToolParsedEventType.ContentDelta) contentSb.Append(tEvent.Content);
                            else toolEvents.Add(tEvent);
                        }
                    }
                }
            }

            foreach (var rChunk in reasoningParser.Flush())
            {
                if (rChunk.Kind == ReasoningChunkKind.Reasoning) reasoningSb.Append(rChunk.Text);
                else
                {
                    foreach (var tEvent in toolParser.ProcessChunk(rChunk.Text))
                    {
                        if (tEvent.Type == ToolParsedEventType.ContentDelta) contentSb.Append(tEvent.Content);
                        else toolEvents.Add(tEvent);
                    }
                }
            }

            foreach (var tEvent in toolParser.Flush())
            {
                if (tEvent.Type == ToolParsedEventType.ContentDelta) contentSb.Append(tEvent.Content);
                else toolEvents.Add(tEvent);
            }

            Assert.Equal("User wants the weather in Tokyo.", reasoningSb.ToString());
            Assert.Equal("I am thinking...  I will now fetch the data.  Processing complete.", contentSb.ToString());
            Assert.True(toolParser.HasToolCalls);

            var header = toolEvents.FirstOrDefault(e => e.Type == ToolParsedEventType.ToolCallHeader);
            Assert.NotNull(header);
            Assert.Equal("get_weather", header.ToolName);

            string fullArgs = string.Join("", toolEvents.Where(e => e.Type == ToolParsedEventType.ToolCallArgumentDelta).Select(e => e.ArgumentDelta));
            using var doc = JsonDocument.Parse(fullArgs);
            Assert.Equal("Tokyo", doc.RootElement.GetProperty("city").GetString());
        }

        // -----------------------------------------------------------------------------------------
        // R-003: Embeddings Semantic Correction & Real Provider Integration Tests
        // -----------------------------------------------------------------------------------------

        [Fact]
        public async Task R003_Embeddings_WithMockProviderRegistered_ReturnsRealVectors()
        {
            var services = new ServiceCollection();
            CliServiceRegistration.ConfigureServices(services);
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
            services.AddSingleton<Claude4NetApiServer>();

            using var sp = services.BuildServiceProvider();
            var server = sp.GetRequiredService<Claude4NetApiServer>();

            int port = GetAvailablePort();
            string apiKey = "test-key-embeddings-123";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var req = new EmbeddingRequest
                {
                    Model = "text-embedding-004",
                    Input = "Test embeddings real provider"
                };

                var resp = await client.PostAsJsonAsync($"http://localhost:{port}/v1/embeddings", req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

                var body = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>();
                Assert.NotNull(body);
                Assert.Single(body.Data);

                var floats = JsonSerializer.Deserialize<List<float>>(body.Data[0].Embedding.ToString()!);
                Assert.NotNull(floats);
                Assert.Equal(TestMockEmbeddingProvider.NativeDimensions, floats.Count);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R003_Embeddings_NoProviderRegistered_ReturnsExplicit501OpenAiError_NoSyntheticFallback()
        {
            var services = new ServiceCollection();
            // Do not register IEmbeddingProvider at all
            services.AddSingleton<Claude4NetApiServer>();

            using var sp = services.BuildServiceProvider();
            var server = sp.GetRequiredService<Claude4NetApiServer>();

            int port = GetAvailablePort();
            string apiKey = "test-key-no-provider-456";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var req = new EmbeddingRequest
                {
                    Model = "text-embedding-004",
                    Input = "Test embeddings missing provider"
                };

                var resp = await client.PostAsJsonAsync($"http://localhost:{port}/v1/embeddings", req);
                Assert.Equal(HttpStatusCode.NotImplemented, resp.StatusCode);

                string json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("unsupported_operation", json);
                Assert.Contains("No active embedding provider is configured", json);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task R003_Embeddings_UnsupportedDimensionRequested_ReturnsExplicit400Error_NoSyntheticScaling()
        {
            var services = new ServiceCollection();
            CliServiceRegistration.ConfigureServices(services);
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
            services.AddSingleton<Claude4NetApiServer>();

            using var sp = services.BuildServiceProvider();
            var server = sp.GetRequiredService<Claude4NetApiServer>();

            int port = GetAvailablePort();
            string apiKey = "test-key-unsupported-dim-789";
            await server.StartAsync(port, apiKey);

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var req = new EmbeddingRequest
                {
                    Model = "text-embedding-004",
                    Input = "Test unsupported dimension scaling",
                    Dimensions = 128 // Native is 768, requested is 128 -> Must be rejected with explicit error!
                };

                var resp = await client.PostAsJsonAsync($"http://localhost:{port}/v1/embeddings", req);
                Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

                string json = await resp.Content.ReadAsStringAsync();
                Assert.Contains("unsupported_dimension", json);
                Assert.Contains("Synthetic dimension scaling is disallowed", json);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        private static int GetAvailablePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
