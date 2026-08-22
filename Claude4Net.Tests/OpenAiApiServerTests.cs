using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class OpenAiApiServerTests : IAsyncLifetime
    {
        private ServiceProvider _serviceProvider = null!;
        private Claude4NetApiServer _server = null!;
        private readonly HttpClient _client = new();
        private int _testPort;
        private const string TestApiKey = "c4n-sk-test-secret-key-12345678";
        private const string RequestContractProviderFailurePrompt = "request-contract-provider-failure";
        private const string RequestContractSensitiveProviderMessage = "REQUEST_CONTRACT_SENSITIVE_PROVIDER_MESSAGE";

        private string? _origCwd;
        private string _origSessionId = null!;
        private string _origActiveProvider = null!;
        private string _origActiveModel = null!;
        private PermissionMode _origPermissionMode;
        private bool _origIsExplicit;

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
            // MockProviderFactory를 먼저 등록하여 우선순위 보장 (FirstOrDefault가 먼저 찾도록)
            services.AddSingleton<IProviderFactory, TestMockProviderFactory>();
            CliServiceRegistration.ConfigureServices(services);
            services.AddSingleton(Wave2TestSupport.CreateOfficialSdkRegistry("mock"));
            services.RemoveAll<IEmbeddingProvider>();
            services.AddSingleton<IEmbeddingProvider, TestMockEmbeddingProvider>();
            services.AddSingleton<Claude4NetApiServer>();

            _serviceProvider = services.BuildServiceProvider();
            _server = _serviceProvider.GetRequiredService<Claude4NetApiServer>();

            _testPort = GetAvailablePort();
            await _server.StartAsync(_testPort, TestApiKey);

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
            var unauthResp = await unauthClient.GetAsync($"http://localhost:{_testPort}/v1/models");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthResp.StatusCode);

            // 2. Wrong token -> 401 Unauthorized
            using var wrongKeyClient = new HttpClient();
            wrongKeyClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "wrong-key-xyz");
            var wrongResp = await wrongKeyClient.GetAsync($"http://localhost:{_testPort}/v1/models");
            Assert.Equal(HttpStatusCode.Unauthorized, wrongResp.StatusCode);

            // 3. Health check allowed without auth -> 200 OK
            var healthResp = await unauthClient.GetAsync($"http://localhost:{_testPort}/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

            // 4. x-api-key header authentication -> 200 OK
            using var xKeyClient = new HttpClient();
            xKeyClient.DefaultRequestHeaders.Add("x-api-key", TestApiKey);
            var xKeyResp = await xKeyClient.GetAsync($"http://localhost:{_testPort}/v1/models");
            Assert.Equal(HttpStatusCode.OK, xKeyResp.StatusCode);
        }

        [Fact]
        public async Task Cors_PreflightOptions_ReturnsOkWithoutAuth()
        {
            using var unauthClient = new HttpClient();
            using var optionsReq = new HttpRequestMessage(HttpMethod.Options, $"http://localhost:{_testPort}/v1/chat/completions");
            optionsReq.Headers.Add("Origin", "http://localhost:3000");
            optionsReq.Headers.Add("Access-Control-Request-Method", "POST");

            var resp = await unauthClient.SendAsync(optionsReq);
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
            Assert.True(resp.Headers.Contains("Access-Control-Allow-Origin") || resp.Headers.Contains("access-control-allow-origin"));
        }

        [Fact]
        public async Task GetModels_ReturnsModelListResponse()
        {
            var response = await _client.GetAsync($"http://localhost:{_testPort}/v1/models");
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
            var resp1 = await _client.GetAsync($"http://localhost:{_testPort}/v1/models/gpt-4o");
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
            var card1 = await resp1.Content.ReadFromJsonAsync<ModelCardDto>();
            Assert.NotNull(card1);
            Assert.Equal("gpt-4o", card1.Id);
            Assert.Equal("openai", card1.OwnedBy);

            // 2. Non-existent model -> 404 with OpenAI error envelope
            var resp2 = await _client.GetAsync($"http://localhost:{_testPort}/v1/models/non-existent-model-xyz");
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

            var resp = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/completions", req);
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
            var response = await _client.GetAsync($"http://localhost:{_testPort}/api/v1/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<ApiStatusResponse>();
            Assert.NotNull(status);
            Assert.Equal("healthy", status.Status);
            // SEC-03: health 엔드포인트는 민감정보 노출 방지를 위해 Port를 반환하지 않음

            var statusResp = await _client.GetAsync($"http://localhost:{_testPort}/api/v1/status");
            Assert.Equal(HttpStatusCode.OK, statusResp.StatusCode);
        }

        [Fact]
        public async Task GetUsage_ReturnsValidUsageMetrics()
        {
            var response = await _client.GetAsync($"http://localhost:{_testPort}/api/v1/usage");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var usage = await response.Content.ReadFromJsonAsync<ApiUsageResponse>();
            Assert.NotNull(usage);
            Assert.True(usage.ContextLimit > 0);
            Assert.NotNull(usage.ContextComponents);
        }

        [Fact]
        public async Task GetTools_ReturnsRegisteredTools()
        {
            var response = await _client.GetAsync($"http://localhost:{_testPort}/api/v1/tools");
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

            var resp1 = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/embeddings", req1);
            Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

            var embedResp1 = await resp1.Content.ReadFromJsonAsync<EmbeddingResponse>();
            Assert.NotNull(embedResp1);
            Assert.Equal("list", embedResp1.Object);
            Assert.Single(embedResp1.Data);

            var vectorFloats = JsonSerializer.Deserialize<List<float>>(embedResp1.Data[0].Embedding.ToString()!);
            Assert.NotNull(vectorFloats);
            Assert.Equal(768, vectorFloats.Count);

            // 2. Batch string array input (default 768)
            var req2 = new EmbeddingRequest
            {
                Model = "text-embedding-004",
                Input = new[] { "First document chunk", "Second document chunk", "Third query vector" }
            };

            var resp2 = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/embeddings", req2);
            Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

            var embedResp2 = await resp2.Content.ReadFromJsonAsync<EmbeddingResponse>();
            Assert.NotNull(embedResp2);
            Assert.Equal(3, embedResp2.Data.Count);
            Assert.Equal(0, embedResp2.Data[0].Index);
            Assert.Equal(1, embedResp2.Data[1].Index);
            Assert.Equal(2, embedResp2.Data[2].Index);

            var batchFloats = JsonSerializer.Deserialize<List<float>>(embedResp2.Data[0].Embedding.ToString()!);
            Assert.NotNull(batchFloats);
            Assert.Equal(768, batchFloats.Count);
        }

        [Fact]
        public async Task Embeddings_Base64EncodingFormat_RoundTripByteDecoding_MatchesFloats()
        {
            var req = new EmbeddingRequest
            {
                Model = "text-embedding-004",
                Input = "Test base64 embedding round-trip decoding",
                Dimensions = 768,
                EncodingFormat = "base64"
            };

            var resp = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/embeddings", req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var embedResp = await resp.Content.ReadFromJsonAsync<EmbeddingResponse>();
            Assert.NotNull(embedResp);
            Assert.Single(embedResp.Data);

            // The embedding property is a Base64 string of IEEE-754 Little-Endian float bytes
            string base64Str = embedResp.Data[0].Embedding.ToString()!;
            Assert.False(string.IsNullOrWhiteSpace(base64Str));

            float[] decodedFloats = Claude4NetApiServer.Base64ToFloats(base64Str);
            Assert.Equal(768, decodedFloats.Length);
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

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
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
        public async Task ChatCompletions_MultimodalArrayContent_ExtractsStructuredPartsAndPreservesImages()
        {
            var msg = new ChatMessageDto
            {
                Role = "user",
                Content = new object[]
                {
                    new { type = "text", text = "Describe this diagram:" },
                    new { type = "image_url", image_url = new { url = "https://example.com/diagram.png", detail = "high" } }
                }
            };

            var parts = msg.GetContentParts();
            Assert.Equal(2, parts.Count);
            Assert.IsType<TextContentPart>(parts[0]);
            Assert.IsType<ImageUrlContentPart>(parts[1]);

            var imgPart = (ImageUrlContentPart)parts[1];
            Assert.Equal("https://example.com/diagram.png", imgPart.ImageUrl.Url);
            Assert.Equal("high", imgPart.ImageUrl.Detail);

            var request = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto> { msg },
                Stream = false
            };

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
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

            var httpResponse = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            var chatResp = await httpResponse.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(chatResp);
            Assert.NotEmpty(chatResp.Choices);

            var choice = chatResp.Choices[0];
            Assert.Equal("tool_calls", choice.FinishReason);
            Assert.NotNull(choice.Message.ToolCalls);
            Assert.NotEmpty(choice.Message.ToolCalls);
            Assert.Equal("calculator", choice.Message.ToolCalls[0].Function.Name);
        }

        [Fact]
        public async Task ChatCompletions_Streaming_ToolCalls_ProgressiveFragmentedArgumentsDelta()
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
                            Description = "Calculates math"
                        }
                    }
                },
                Stream = true,
                StreamOptions = new StreamOptionsDto { IncludeUsage = true }
            };

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_testPort}/v1/chat/completions")
            {
                Content = JsonContent.Create(request)
            };

            var httpResponse = await _client.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);

            using var stream = await httpResponse.Content.ReadAsStreamAsync();
            using var reader = new System.IO.StreamReader(stream);

            var receivedChunks = new List<string>();
            bool sawToolName = false;
            bool sawToolArgsDelta = false;
            bool sawToolFinishReason = false;

            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("data: "))
                {
                    receivedChunks.Add(line);
                    if (line.Contains("[DONE]")) break;

                    string dataJson = line.Substring("data: ".Length);
                    if (dataJson.Contains("\"name\":\"calculator\""))
                    {
                        sawToolName = true;
                    }
                    if (dataJson.Contains("\"arguments\":\""))
                    {
                        sawToolArgsDelta = true;
                    }
                    if (dataJson.Contains("\"finish_reason\":\"tool_calls\""))
                    {
                        sawToolFinishReason = true;
                    }
                }
            }

            Assert.True(sawToolName, "Expected tool name in header chunk");
            Assert.True(sawToolArgsDelta, "Expected arguments fragmented deltas in stream");
            Assert.True(sawToolFinishReason, "Expected finish_reason: tool_calls");
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

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_testPort}/v1/chat/completions")
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
        public async Task ChatCompletions_ForwardCompatibility_UnknownJsonProperties_DoNotFail()
        {
            string payload = @"{
                ""model"": ""gpt-4o"",
                ""messages"": [
                    { ""role"": ""user"", ""content"": ""Hello forward compat"", ""unknown_metadata"": { ""custom_flag"": true } }
                ],
                ""stream"": false,
                ""future_optional_openai_feature"": 12345,
                ""service_tier"": ""auto"",
                ""parallel_tool_calls"": true
            }";

            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_testPort}/v1/chat/completions")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            var httpResponse = await _client.SendAsync(reqMsg);
            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        }

        [Fact]
        public void ChatCompletions_StopSequences_TruncatesOutput()
        {
            string raw = "Hello world! This is a stop sequence test.";
            string truncated = Claude4NetApiServer.ApplyStopSequences(raw, "stop sequence");
            Assert.Equal("Hello world! This is a ", truncated);

            string arrayTruncated = Claude4NetApiServer.ApplyStopSequences(raw, new[] { "not_found", "test" });
            Assert.Equal("Hello world! This is a stop sequence ", arrayTruncated);
        }

        [Fact]
        public async Task ChatCompletions_ResponseFormat_JsonObject_EnforcesJsonPrompting()
        {
            var req = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = "Give me user data" }
                },
                ResponseFormat = new ResponseFormatDto { Type = "json_object" },
                Stream = false
            };

            var resp = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", req);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task ChatCompletions_RequestContract_RejectsUnsupportedTemperatureAndAcceptsDefault()
        {
            var defaultTemperatureRequest = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "Default temperature" } },
                Temperature = 0.7
            };

            var defaultTemperatureResponse = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", defaultTemperatureRequest);
            Assert.Equal(HttpStatusCode.OK, defaultTemperatureResponse.StatusCode);

            var unsupportedTemperatureRequest = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "Unsupported temperature" } },
                Temperature = 2.5
            };

            var unsupportedTemperatureResponse = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", unsupportedTemperatureRequest);
            Assert.Equal(HttpStatusCode.BadRequest, unsupportedTemperatureResponse.StatusCode);

            using var errorDocument = JsonDocument.Parse(await unsupportedTemperatureResponse.Content.ReadAsStringAsync());
            Assert.Equal("temperature", errorDocument.RootElement.GetProperty("error").GetProperty("param").GetString());
        }

        [Fact]
        public async Task ChatCompletions_RequestContract_AppliesStopToNonStreamingResponse()
        {
            const string stopSequence = "REQUEST_CONTRACT_STOP";
            var request = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = $"Before {stopSequence} after" }
                },
                Stop = stopSequence
            };

            var response = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(completion);
            Assert.Equal("stop", completion.Choices[0].FinishReason);
            Assert.DoesNotContain(stopSequence, completion.Choices[0].Message.GetContentString());
        }

        [Fact]
        public async Task ChatCompletions_RequestContract_ResponseFormatJsonObjectChangesEchoedPrompt()
        {
            var request = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "Return account details" } },
                ResponseFormat = new ResponseFormatDto { Type = "json_object" }
            };

            var response = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var completion = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>();
            Assert.NotNull(completion);
            Assert.Contains("You MUST format your response as a valid JSON object.", completion.Choices[0].Message.GetContentString());
        }

        [Fact]
        public async Task ChatCompletions_RequestContract_MalformedJsonReturnsSanitizedErrorEnvelope()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_testPort}/v1/chat/completions")
            {
                Content = new StringContent("{\"model\":\"gpt-4o\",\"messages\":[", Encoding.UTF8, "application/json")
            };

            var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            string body = await response.Content.ReadAsStringAsync();
            using var errorDocument = JsonDocument.Parse(body);
            JsonElement error = errorDocument.RootElement.GetProperty("error");
            Assert.Equal("Invalid JSON payload.", error.GetProperty("message").GetString());
            Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
            Assert.Equal("invalid_json", error.GetProperty("code").GetString());
            Assert.DoesNotContain("Path:", body);
        }

        [Fact]
        public async Task ChatCompletions_RequestContract_ProviderExceptionReturnsSanitizedProviderError()
        {
            var request = new ChatCompletionRequest
            {
                Model = "gpt-4o",
                Messages = new List<ChatMessageDto>
                {
                    new() { Role = "user", Content = RequestContractProviderFailurePrompt }
                }
            };

            var response = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", request);
            string body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(RequestContractSensitiveProviderMessage, body);
            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

            using var errorDocument = JsonDocument.Parse(body);
            JsonElement error = errorDocument.RootElement.GetProperty("error");
            Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
            Assert.Equal("provider_error", error.GetProperty("type").GetString());
            Assert.Equal("provider_error", error.GetProperty("code").GetString());
        }

        [Fact]
        public async Task TextCompletions_RequestContract_StreamReturnsSseChunksAndSingleDone()
        {
            var request = new TextCompletionRequest
            {
                Model = "gpt-4o",
                Prompt = "Legacy stream contract",
                Stream = true
            };

            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{_testPort}/v1/completions")
            {
                Content = JsonContent.Create(request)
            };
            var response = await _client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

            string body = await response.Content.ReadAsStringAsync();
            var payloads = body.Split('\n')
                .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
                .Select(line => line["data: ".Length..].Trim())
                .ToList();
            var completionChunks = payloads.Where(payload => payload != "[DONE]").ToList();

            Assert.NotEmpty(completionChunks);
            Assert.Equal(1, payloads.Count(payload => payload == "[DONE]"));
            Assert.All(completionChunks, payload =>
            {
                using var chunkDocument = JsonDocument.Parse(payload);
                Assert.Equal("text_completion", chunkDocument.RootElement.GetProperty("object").GetString());
            });
        }

        [Fact]
        public async Task ChatCompletions_ClientCancellation_AbortsCleanly()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            var req = new ChatCompletionRequest
            {
                Model = "claude-3-5-sonnet",
                Messages = new List<ChatMessageDto> { new() { Role = "user", Content = "Cancel test" } },
                Stream = true
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", req, cts.Token);
            });
        }

        [Fact]
        public async Task ChatCompletions_ConcurrentRequests_HandleLoadWithoutRaceConditions()
        {
            var tasks = Enumerable.Range(1, 10).Select(async i =>
            {
                var req = new ChatCompletionRequest
                {
                    Model = "claude-3-5-sonnet",
                    Messages = new List<ChatMessageDto>
                    {
                        new() { Role = "user", Content = $"Concurrent request {i}" }
                    },
                    Stream = false
                };

                var resp = await _client.PostAsJsonAsync($"http://localhost:{_testPort}/v1/chat/completions", req);
                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                var body = await resp.Content.ReadFromJsonAsync<ChatCompletionResponse>();
                Assert.NotNull(body);
                Assert.NotEmpty(body.Choices);
            });

            await Task.WhenAll(tasks);
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
        public void CliOptions_ApiKeyEnvironmentVariable_ResolvesKeyWithoutWarning()
        {
            string environmentVariable = "C4N_TEST_API_KEY_" + Guid.NewGuid().ToString("N");
            string apiKey = "c4n-env-secret-" + Guid.NewGuid().ToString("N");
            string? originalValue = Environment.GetEnvironmentVariable(environmentVariable);
            Environment.SetEnvironmentVariable(environmentVariable, apiKey);
            try
            {
                CliOptions options = CliOptions.Parse(new[] { "--api-key-env", environmentVariable });

                Assert.Equal(apiKey, options.ApiKey);
                Assert.Empty(GetWarnings(options));
                Assert.Null(options.ValidationError);
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            }
        }

        [Fact]
        public void CliOptions_LiteralApiKey_RemainsSupportedWithNonSecretDeprecation()
        {
            const string warning = "--api-key is deprecated; use --api-key-env <NAME>.";
            string apiKey = "c4n-literal-secret-" + Guid.NewGuid().ToString("N");

            CliOptions options = CliOptions.Parse(new[] { "--api-key", apiKey });
            IReadOnlyList<string> warnings = GetWarnings(options);
            string diagnostics = JsonSerializer.Serialize(new
            {
                options.ValidationError,
                Warnings = warnings,
                options.RemainingArgs
            });

            Assert.Equal(apiKey, options.ApiKey);
            Assert.Equal(new[] { warning }, warnings);
            Assert.DoesNotContain(apiKey, warning, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, diagnostics, StringComparison.Ordinal);
        }

        [Fact]
        public void CliOptions_ApiKeyEnvironmentVariable_MissingValueFailsWithoutEchoingNameValue()
        {
            const string expectedError = "The environment variable specified by --api-key-env is not set or is empty.";
            string environmentVariable = "C4N_TEST_MISSING_API_KEY_" + Guid.NewGuid().ToString("N");
            string? originalValue = Environment.GetEnvironmentVariable(environmentVariable);
            Environment.SetEnvironmentVariable(environmentVariable, null);
            try
            {
                CliOptions options = CliOptions.Parse(new[] { "--api-key-env", environmentVariable });
                IReadOnlyList<string> warnings = GetWarnings(options);
                string diagnostics = JsonSerializer.Serialize(new
                {
                    options.ValidationError,
                    Warnings = warnings,
                    options.RemainingArgs
                });

                Type? guardType = typeof(CliOptions).Assembly.GetType("Claude4Net.Cli.Bootstrap.CliStartupGuard");
                Assert.NotNull(guardType);
                MethodInfo? validate = guardType!.GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(validate);
                using var errorOutput = new StringWriter();
                int exitCode = Assert.IsType<int>(validate!.Invoke(null, new object[] { options, errorOutput }));

                Assert.Null(options.ApiKey);
                Assert.Equal(expectedError, options.ValidationError);
                Assert.Empty(warnings);
                Assert.NotEqual(0, exitCode);
                Assert.Contains(expectedError, errorOutput.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(environmentVariable, diagnostics + errorOutput, StringComparison.Ordinal);

                CliOptions softMigrationWarning = CliOptions.Parse(new[] { "--dashboard" });
                using var softWarningOutput = new StringWriter();
                int softWarningExitCode = Assert.IsType<int>(validate.Invoke(
                    null,
                    new object[] { softMigrationWarning, softWarningOutput }));
                Assert.Equal(0, softWarningExitCode);
                Assert.NotNull(softMigrationWarning.ValidationError);
                Assert.Equal(string.Empty, softWarningOutput.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            }
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
            Assert.Contains("native provider dimensions", start);
            Assert.DoesNotContain("1536-dim multi-provider vector router", start);
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

        private static IReadOnlyList<string> GetWarnings(CliOptions options)
        {
            var property = typeof(CliOptions).GetProperty("Warnings");
            Assert.NotNull(property);
            return Assert.IsAssignableFrom<IReadOnlyList<string>>(property!.GetValue(options));
        }

        private class TestMockProviderFactory : IProviderFactory
        {
            public string TransportKind => "mock";
            public bool SupportsApiRequests => true;

            public bool CanCreate(ProviderDescriptor descriptor) => true;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                return new TestMockProvider();
            }

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
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
                if (prompt.Contains(RequestContractProviderFailurePrompt, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(RequestContractSensitiveProviderMessage);
                }
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
