using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.Runtime.ApiServer.Streaming;
using Claude4Net.Runtime.Services;
using Claude4Net.SDK;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Claude4Net.Runtime.ApiServer
{
    /// <summary>
    /// In-Process OpenAI-Compatible and Claude4Net Custom HTTP API Server (Kestrel / Minimal API).
    /// </summary>
    public class Claude4NetApiServer : IDisposable, IAsyncDisposable
    {
        private WebApplication? _host;
        private ApiModelCatalog? _apiModelCatalog;
        private readonly IServiceProvider _serviceProvider;
        private readonly ProviderRegistry? _providerRegistry;
        private readonly IReadOnlyList<IEmbeddingProvider> _embeddingProviders;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
        private string _bindAddress = IPAddress.Loopback.ToString();
        private string _scheme = Uri.UriSchemeHttp;
        private X509Certificate2? _serverCertificate;
        private int _isApiKeyAvailableForDisplay;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public const int DefaultPort = 7836;
        public int Port { get; private set; } = DefaultPort;
        public string ApiKey { get; private set; } = string.Empty;
        public bool IsRunning => _host != null;
        public string Url => $"{_scheme}://{FormatAddressForUrl(_bindAddress)}:{Port}";

        public Claude4NetApiServer(
            IServiceProvider serviceProvider,
            ProviderRegistry? providerRegistry = null,
            IEnumerable<IEmbeddingProvider>? embeddingProviders = null)
        {
            _serviceProvider = serviceProvider;
            _providerRegistry = providerRegistry ?? serviceProvider.GetService<ProviderRegistry>();
            _embeddingProviders = embeddingProviders?.ToArray() ?? Array.Empty<IEmbeddingProvider>();
        }

        public Task StartAsync(int port = DefaultPort, string? apiKey = null, CancellationToken ct = default)
            => StartAsync(new Claude4NetApiServerOptions { Port = port, ApiKey = apiKey }, ct);

        public async Task StartAsync(Claude4NetApiServerOptions options, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            await _lifecycleGate.WaitAsync(ct);
            try
            {
                if (_host != null)
                {
                    return;
                }

                Interlocked.Exchange(ref _isApiKeyAvailableForDisplay, 0);
                ValidatedClaude4NetApiServerOptions validatedOptions = options.Validate();
                string apiKey = !string.IsNullOrWhiteSpace(validatedOptions.ApiKey)
                    ? validatedOptions.ApiKey
                    : "c4n-sk-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];

                WebApplication? app = null;
                try
                {
                    _apiModelCatalog = ApiModelCatalog.Build(
                        _serviceProvider,
                        _providerRegistry,
                        _embeddingProviders);
                    var builder = WebApplication.CreateBuilder();
                    builder.WebHost.ConfigureKestrel(kestrel =>
                    {
                        kestrel.Limits.MaxRequestBodySize = validatedOptions.MaxRequestBodyBytes;
                        kestrel.Listen(validatedOptions.BindAddress, validatedOptions.Port, listenOptions =>
                        {
                            if (validatedOptions.Certificate != null)
                            {
                                listenOptions.UseHttps(validatedOptions.Certificate);
                            }
                        });
                    });

                    builder.Services.AddCors(cors =>
                    {
                        cors.AddDefaultPolicy(policy =>
                        {
                            policy.SetIsOriginAllowed(IsAllowedCorsOrigin)
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                        });
                    });

                    app = builder.Build();
                    app.UseCors();
                    ConfigureRequestPipeline(app, validatedOptions, apiKey);

                    MapOpenAiEndpoints(app);
                    MapCustomEndpoints(app);

                    await app.StartAsync(ct);
                    Port = validatedOptions.Port;
                    ApiKey = apiKey;
                    _bindAddress = validatedOptions.BindAddress.ToString();
                    _scheme = validatedOptions.Scheme;
                    _serverCertificate = validatedOptions.Certificate;
                    _host = app;
                    Interlocked.Exchange(ref _isApiKeyAvailableForDisplay, 1);
                }
                catch
                {
                    _apiModelCatalog = null;
                    Interlocked.Exchange(ref _isApiKeyAvailableForDisplay, 0);
                    if (app != null) await app.DisposeAsync();
                    validatedOptions.Certificate?.Dispose();
                    throw;
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            await _lifecycleGate.WaitAsync(ct);
            try
            {
                Interlocked.Exchange(ref _isApiKeyAvailableForDisplay, 0);
                WebApplication? host = _host;
                X509Certificate2? serverCertificate = _serverCertificate;
                _host = null;
                ApiKey = string.Empty;
                _apiModelCatalog = null;
                _serverCertificate = null;
                if (host == null)
                {
                    serverCertificate?.Dispose();
                    return;
                }

                try
                {
                    await host.StopAsync(ct);
                }
                finally
                {
                    await host.DisposeAsync();
                    serverCertificate?.Dispose();
                }
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }

        public string? TakeApiKeyForDisplay()
        {
            return Interlocked.Exchange(ref _isApiKeyAvailableForDisplay, 0) == 1
                ? ApiKey
                : null;
        }

        public ValueTask DisposeAsync() => new(StopAsync());

        public void Dispose() => StopAsync().GetAwaiter().GetResult();

        private static void ConfigureRequestPipeline(
            WebApplication app,
            ValidatedClaude4NetApiServerOptions options,
            string apiKey)
        {
            var executionSlots = new SemaphoreSlim(options.MaxConcurrentRequests, options.MaxConcurrentRequests);
            var admissionSlots = new SemaphoreSlim(
                options.MaxConcurrentRequests + options.MaxQueuedRequests,
                options.MaxConcurrentRequests + options.MaxQueuedRequests);

            app.Use((context, next) => ExecuteProtectedRequestAsync(
                context,
                next,
                options,
                apiKey,
                executionSlots,
                admissionSlots));
        }

        private static async Task ExecuteProtectedRequestAsync(
            HttpContext context,
            RequestDelegate next,
            ValidatedClaude4NetApiServerOptions options,
            string apiKey,
            SemaphoreSlim executionSlots,
            SemaphoreSlim admissionSlots)
        {
            if (HttpMethods.IsOptions(context.Request.Method) || IsHealthRequest(context.Request.Path))
            {
                await next(context);
                return;
            }

            if (!IsAuthenticated(context, apiKey))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Invalid or missing API key. Provide header 'Authorization: Bearer <key>' or 'x-api-key: <key>'.",
                    "authentication_error",
                    "invalid_api_key",
                    context.RequestAborted);
                return;
            }

            if (context.Request.ContentLength > options.MaxRequestBodyBytes)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "The request body exceeds the configured size limit.",
                    "invalid_request_error",
                    "request_too_large",
                    context.RequestAborted);
                return;
            }

            if (!admissionSlots.Wait(0))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "The server has reached its concurrent request limit.",
                    "rate_limit_error",
                    "concurrency_limit_exceeded",
                    context.RequestAborted);
                return;
            }

            bool executionSlotAcquired = false;
            CancellationToken clientAbort = context.RequestAborted;
            using var timeoutCancellation = new CancellationTokenSource(options.RequestTimeout);
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                clientAbort,
                timeoutCancellation.Token);
            context.RequestAborted = requestCancellation.Token;

            try
            {
                await executionSlots.WaitAsync(context.RequestAborted);
                executionSlotAcquired = true;
                await next(context);
            }
            catch (BadHttpRequestException error) when (
                error.StatusCode == StatusCodes.Status413PayloadTooLarge &&
                !context.Response.HasStarted)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "The request body exceeds the configured size limit.",
                    "invalid_request_error",
                    "request_too_large",
                    clientAbort);
            }
            catch (OperationCanceledException) when (
                timeoutCancellation.IsCancellationRequested &&
                !clientAbort.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                {
                    await WriteErrorAsync(
                        context,
                        StatusCodes.Status504GatewayTimeout,
                        "The request exceeded the configured timeout.",
                        "timeout_error",
                        "request_timeout",
                        clientAbort);
                }
                else
                {
                    context.Abort();
                }
            }
            finally
            {
                context.RequestAborted = clientAbort;
                if (executionSlotAcquired) executionSlots.Release();
                admissionSlots.Release();
            }
        }

        private static string ExtractAuthToken(HttpContext context)
        {
            string authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authorization["Bearer ".Length..].Trim();
            }
            if (context.Request.Headers.TryGetValue("x-api-key", out var key))
            {
                return key.ToString().Trim();
            }
            return string.Empty;
        }

        private static bool IsAuthenticated(HttpContext context, string apiKey)
        {
            string token = ExtractAuthToken(context);
            return string.Equals(token, apiKey, StringComparison.Ordinal);
        }

        private static bool IsHealthRequest(PathString path) =>
            path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/version", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/tags", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/show", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/props", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/v1/props", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase);

        private static bool IsAllowedCorsOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                return false;
            }

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        }

        private static string FormatAddressForUrl(string address) =>
            address.Contains(':', StringComparison.Ordinal) ? $"[{address}]" : address;

        private static Task WriteErrorAsync(
            HttpContext context,
            int statusCode,
            string message,
            string type,
            string code,
            CancellationToken cancellationToken)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            string json = JsonSerializer.Serialize(
                new OpenAiErrorEnvelope(new OpenAiError(message, type, code)),
                _jsonOptions);
            return context.Response.WriteAsync(json, cancellationToken);
        }

        private void MapOpenAiEndpoints(WebApplication app)
        {
            // GET /v1/models & /api/v1/models
            Delegate handleModels = () => Results.Ok(new ModelListResponse { Data = Catalog.Models.ToList() });
            app.MapGet("/v1/models", handleModels);
            app.MapGet("/api/v1/models", handleModels);

            // GET /v1/models/{modelId} & /api/v1/models/{modelId}
            Delegate handleModelById = (string modelId) =>
            {
                if (Catalog.TryGetModel(modelId, out ModelCardDto found))
                {
                    return Results.Ok(found);
                }

                return Results.NotFound(new
                {
                    error = new
                    {
                        message = $"The model '{modelId}' does not exist.",
                        type = "invalid_request_error",
                        param = "model",
                        code = "model_not_found"
                    }
                });
            };
            app.MapGet("/v1/models/{*modelId}", handleModelById);
            app.MapGet("/api/v1/models/{*modelId}", handleModelById);

            // POST /v1/completions & /api/v1/completions (Legacy text completion endpoint)
            Func<HttpContext, Task<IResult>> handleCompletions = async (HttpContext context) =>
            {
                TextCompletionRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<TextCompletionRequest>(
                        context.Request.Body,
                        _jsonOptions,
                        context.RequestAborted);
                }
                catch (JsonException)
                {
                    return InvalidJsonError();
                }

                OpenAiRequestValidationError? structureError = OpenAiRequestValidator.Validate(request);
                if (structureError is not null)
                {
                    return InvalidRequest(structureError);
                }

                var validationError = ValidateKnownOptions(request!);
                if (validationError != null)
                {
                    return validationError;
                }

                var (providerLease, resolvedModel) = ResolveRequestProviderAndModel(request!.Model);
                if (providerLease == null)
                {
                    return ModelNotFoundError(request.Model);
                }
                await using var requestProviderLease = providerLease;
                ILLMProvider provider = requestProviderLease.Provider;

                string prompt = request.GetPromptString();
                string completionId = "cmpl-" + Guid.NewGuid().ToString("N")[..12];

                if (request.Stream)
                {
                    var ct = context.RequestAborted;
                    var enumerator = provider.StreamQueryAsync(prompt, model: resolvedModel, ct: ct).GetAsyncEnumerator(ct);
                    try
                    {
                    bool hasEvent;
                    try
                    {
                        hasEvent = await MoveNextWithCancellationAsync(enumerator, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        return ProviderError();
                    }

                    context.Response.ContentType = "text/event-stream; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers.Connection = "keep-alive";

                    string finishReason = "stop";
                    var emittedResponse = new StringBuilder();
                    try
                    {
                        while (hasEvent)
                        {
                            var streamEvent = enumerator.Current;
                            if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                string candidate = emittedResponse + streamEvent.Delta;
                                string capped = ApplyTokenLimit(candidate, request.MaxTokens, provider.TokenCounter, out bool wasTruncated);
                                string delta = capped[emittedResponse.Length..];
                                if (delta.Length > 0)
                                {
                                    await WriteSseAsync(context, CreateTextCompletionResponse(completionId, resolvedModel, delta, null), ct);
                                    emittedResponse.Append(delta);
                                }
                                if (wasTruncated)
                                {
                                    finishReason = "length";
                                    break;
                                }
                            }
                            hasEvent = await MoveNextWithCancellationAsync(enumerator, ct);
                        }

                        await WriteSseAsync(context, CreateTextCompletionResponse(completionId, resolvedModel, string.Empty, finishReason), ct);
                        await context.Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8, ct);
                        await context.Response.Body.FlushAsync(ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }

                    return Results.Empty;
                    }
                    finally
                    {
                        await DisposeEnumeratorAsync(enumerator, ct);
                    }
                }

                try
                {
                    var sbResponse = new StringBuilder();
                    var ct = context.RequestAborted;
                    var enumerator = provider.StreamQueryAsync(prompt, model: resolvedModel, ct: ct).GetAsyncEnumerator(ct);
                    try
                    {
                        while (await MoveNextWithCancellationAsync(enumerator, ct))
                        {
                            var streamEvent = enumerator.Current;
                            if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                sbResponse.Append(streamEvent.Delta);
                            }
                        }

                        string responseText = StopSequenceHelper.Apply(sbResponse.ToString(), request.Stop);
                        responseText = ApplyTokenLimit(responseText, request.MaxTokens, provider.TokenCounter, out bool wasTruncated);
                        int promptTokens = provider.TokenCounter.CountTokens(prompt);
                        int completionTokens = provider.TokenCounter.CountTokens(responseText);

                        var response = new TextCompletionResponse
                        {
                            Id = completionId,
                            Model = resolvedModel,
                            Choices = new List<TextChoiceDto>
                            {
                                new()
                                {
                                    Text = responseText,
                                    Index = 0,
                                    FinishReason = wasTruncated ? "length" : "stop"
                                }
                            },
                            Usage = new CompletionUsageDto
                            {
                                PromptTokens = promptTokens,
                                CompletionTokens = completionTokens
                            }
                        };

                        return Results.Json(response, _jsonOptions);
                    }
                    finally
                    {
                        await DisposeEnumeratorAsync(enumerator, ct);
                    }
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return ProviderError();
                }
            };
            app.MapPost("/v1/completions", handleCompletions);
            app.MapPost("/api/v1/completions", handleCompletions);

            // POST /v1/embeddings & /api/v1/embeddings
            Func<HttpContext, Task<IResult>> handleEmbeddings = async (HttpContext context) =>
            {
                EmbeddingRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<EmbeddingRequest>(
                        context.Request.Body,
                        _jsonOptions,
                        context.RequestAborted);
                }
                catch (JsonException)
                {
                    return InvalidJsonError();
                }

                OpenAiRequestValidationError? structureError = OpenAiRequestValidator.Validate(request);
                if (structureError is not null) return InvalidRequest(structureError);

                if (!Catalog.HasEmbeddingProviders)
                {
                    return Results.Json(new
                    {
                        error = new
                        {
                            message = "No active embedding provider is configured.",
                            type = "invalid_request_error",
                            code = "unsupported_operation"
                        }
                    }, _jsonOptions, statusCode: StatusCodes.Status501NotImplemented);
                }

                if (!Catalog.TryGetEmbeddingProvider(request!.Model, out IEmbeddingProvider embeddingProvider))
                {
                    return ModelNotFoundError(request.Model);
                }

                var inputs = request.GetInputs();
                if (inputs.Count == 0)
                {
                    return Results.BadRequest(new { error = new { message = "Input field cannot be empty.", type = "invalid_request_error" } });
                }

                string encodingFormat = request.EncodingFormat ?? "float";
                bool useBase64 = string.Equals(encodingFormat, "base64", StringComparison.OrdinalIgnoreCase);
                if (!useBase64 && !string.Equals(encodingFormat, "float", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.BadRequest(new
                    {
                        error = new
                        {
                            message = "encoding_format must be either 'float' or 'base64'.",
                            type = "invalid_request_error",
                            param = "encoding_format",
                            code = "invalid_value"
                        }
                    });
                }

                var vectors = new List<float[]>(inputs.Count);
                try
                {
                    foreach (string input in inputs)
                    {
                        vectors.Add(await embeddingProvider.GetEmbeddingAsync(input, context.RequestAborted));
                    }
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return EmbeddingProviderError();
                }

                int nativeDimensions = vectors[0]?.Length ?? 0;
                if (nativeDimensions == 0 || vectors.Any(vector =>
                        vector == null ||
                        vector.Length != nativeDimensions ||
                        vector.Any(value => float.IsNaN(value) || float.IsInfinity(value))))
                {
                    return EmbeddingProviderError();
                }

                if (request.Dimensions.HasValue && request.Dimensions.Value != nativeDimensions)
                {
                    return Results.BadRequest(new
                    {
                        error = new
                        {
                            message = $"Synthetic dimension scaling is disallowed. The provider returned {nativeDimensions} dimensions, but {request.Dimensions.Value} were requested.",
                            type = "invalid_request_error",
                            param = "dimensions",
                            code = "unsupported_dimension"
                        }
                    });
                }

                var response = new EmbeddingResponse
                {
                    Model = embeddingProvider.ModelId,
                    Data = new List<EmbeddingData>()
                };

                int totalTokens = 0;
                for (int i = 0; i < inputs.Count; i++)
                {
                    string text = inputs[i];
                    totalTokens += Math.Max(1, text.Length / 4);
                    response.Data.Add(new EmbeddingData
                    {
                        Index = i,
                        Embedding = useBase64 ? EmbeddingUtils.FloatsToBase64(vectors[i]) : vectors[i]
                    });
                }

                response.Usage = new EmbeddingUsage
                {
                    PromptTokens = totalTokens
                };

                return Results.Json(response, _jsonOptions);
            };

            static IResult EmbeddingProviderError()
            {
                return Results.Json(new
                {
                    error = new
                    {
                        message = "The embedding provider returned an invalid response.",
                        type = "provider_error",
                        code = "provider_error"
                    }
                }, _jsonOptions, statusCode: StatusCodes.Status502BadGateway);
            }
            app.MapPost("/v1/embeddings", handleEmbeddings);
            app.MapPost("/api/v1/embeddings", handleEmbeddings);

            // POST /v1/chat/completions & /api/v1/chat/completions
            Func<HttpContext, Task<IResult>> handleChatCompletions = async (HttpContext context) =>
            {
                ChatCompletionRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<ChatCompletionRequest>(
                        context.Request.Body,
                        _jsonOptions,
                        context.RequestAborted);
                }
                catch (JsonException)
                {
                    return InvalidJsonError();
                }

                OpenAiRequestValidationError? structureError = OpenAiRequestValidator.Validate(request);
                if (structureError is not null)
                {
                    return InvalidRequest(structureError);
                }

                var validationError = ValidateKnownOptions(request!);
                if (validationError != null)
                {
                    return validationError;
                }

                var (providerLease, resolvedModel) = ResolveRequestProviderAndModel(request!.Model);
                if (providerLease == null)
                {
                    return ModelNotFoundError(request.Model);
                }
                await using var requestProviderLease = providerLease;
                ILLMProvider provider = requestProviderLease.Provider;

                string prompt = PromptBuilder.BuildFromMessages(request.Messages, request.Tools, request.ResponseFormat);
                string completionId = "chatcmpl-" + Guid.NewGuid().ToString("N")[..12];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var declaredToolNames = request.Tools?
                    .Select(tool => tool.Function.Name)
                    .ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

                if (request.Stream)
                {
                    var ct = context.RequestAborted;
                    var streamEnumerator = provider.StreamQueryAsync(prompt, model: resolvedModel, ct: ct).GetAsyncEnumerator(ct);
                    try
                    {
                    bool hasStreamEvent;
                    try
                    {
                        hasStreamEvent = await MoveNextWithCancellationAsync(streamEnumerator, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return ProviderError(ex);
                    }

                    context.Response.ContentType = "text/event-stream; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers.Connection = "keep-alive";

                    int promptTokens = provider.TokenCounter.CountTokens(prompt);
                    var reasoningParser = new IncrementalReasoningParser();
                    var toolParser = new IncrementalToolCallParser();
                    var emittedText = new StringBuilder();
                    var acceptedFallbackToolIndices = new HashSet<int>();
                    bool toolsDeclared = declaredToolNames.Count > 0;
                    bool hasEmittedToolCall = false;
                    bool hasNativeToolCall = false;
                    int nextToolIndex = 0;

                    async Task WriteChunkAsync(ChatChunkDeltaDto delta, string? finishReason = null)
                    {
                        var chunk = new ChatCompletionChunk
                        {
                            Id = completionId,
                            Model = resolvedModel,
                            Choices = new List<ChatChunkChoiceDto>
                            {
                                new() { Index = 0, Delta = delta, FinishReason = finishReason }
                            }
                        };
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                        await context.Response.Body.FlushAsync(ct);
                    }

                    async Task EmitContentAsync(string? content)
                    {
                        if (string.IsNullOrEmpty(content)) return;
                        emittedText.Append(content);
                        await WriteChunkAsync(new ChatChunkDeltaDto { Content = content });
                    }

                    async Task EmitReasoningAsync(string? reasoning)
                    {
                        if (string.IsNullOrEmpty(reasoning)) return;
                        emittedText.Append(reasoning);
                        await WriteChunkAsync(new ChatChunkDeltaDto { ReasoningContent = reasoning });
                    }

                    async Task EmitToolEventAsync(ToolParsedEvent parsedEvent)
                    {
                        if (parsedEvent.Type == ToolParsedEventType.ContentDelta)
                        {
                            await EmitContentAsync(parsedEvent.Content);
                            return;
                        }

                        if (hasNativeToolCall) return;

                        if (parsedEvent.Type == ToolParsedEventType.ToolCallHeader)
                        {
                            if (!IsDeclaredToolCall(parsedEvent.ToolName, declaredToolNames)) return;

                            acceptedFallbackToolIndices.Add(parsedEvent.ToolIndex);
                            nextToolIndex = Math.Max(nextToolIndex, parsedEvent.ToolIndex + 1);
                            hasEmittedToolCall = true;
                            emittedText.Append(parsedEvent.ToolName);
                            await WriteChunkAsync(new ChatChunkDeltaDto
                            {
                                ToolCalls = new List<ToolCallDto>
                                {
                                    new()
                                    {
                                        Index = parsedEvent.ToolIndex,
                                        Id = parsedEvent.ToolId,
                                        Function = new FunctionCallDto { Name = parsedEvent.ToolName, Arguments = string.Empty }
                                    }
                                }
                            });
                            return;
                        }

                        if (!acceptedFallbackToolIndices.Contains(parsedEvent.ToolIndex)) return;

                        string arguments = parsedEvent.ArgumentDelta ?? string.Empty;
                        emittedText.Append(arguments);
                        await WriteChunkAsync(new ChatChunkDeltaDto
                        {
                            ToolCalls = new List<ToolCallDto>
                            {
                                new()
                                {
                                    Index = parsedEvent.ToolIndex,
                                    Type = null,
                                    Function = new FunctionCallDto { Arguments = arguments }
                                }
                            }
                        });
                    }

                    async Task ProcessContentAsync(string content)
                    {
                        if (!toolsDeclared)
                        {
                            await EmitContentAsync(content);
                            return;
                        }

                        foreach (var parsedEvent in toolParser.ProcessChunk(content))
                        {
                            await EmitToolEventAsync(parsedEvent);
                        }
                    }

                    async Task ProcessReasoningChunkAsync(ReasoningParsedChunk parsedChunk)
                    {
                        if (parsedChunk.Kind == ReasoningChunkKind.Reasoning)
                        {
                            await EmitReasoningAsync(parsedChunk.Text);
                        }
                        else
                        {
                            await ProcessContentAsync(parsedChunk.Text);
                        }
                    }

                    static string RemoveTrailingMarkupPrefix(string text)
                    {
                        string[] tags = { "<think>", "</think>", "<invoke", "</invoke>", "<parameter", "</parameter>" };
                        foreach (string tag in tags)
                        {
                            int maxLength = Math.Min(text.Length, tag.Length - 1);
                            for (int length = maxLength; length > 0; length--)
                            {
                                if (tag.StartsWith(text[^length..], StringComparison.OrdinalIgnoreCase))
                                {
                                    return text[..^length];
                                }
                            }
                        }
                        return text;
                    }

                    try
                    {
                        await WriteChunkAsync(new ChatChunkDeltaDto { Role = "assistant" });

                        while (hasStreamEvent)
                        {
                            var streamEvent = streamEnumerator.Current;
                            if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                foreach (var parsedChunk in reasoningParser.ProcessChunk(streamEvent.Delta))
                                {
                                    await ProcessReasoningChunkAsync(parsedChunk);
                                }
                            }
                            else if (streamEvent.Type == LLMStreamEventType.ThinkingDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                await EmitReasoningAsync(streamEvent.Delta);
                            }
                            else if (streamEvent.Type == LLMStreamEventType.ToolCallStart && streamEvent.ToolCall != null)
                            {
                                var toolCall = streamEvent.ToolCall;
                                if (IsDeclaredToolCall(toolCall.Name, declaredToolNames))
                                {
                                    hasNativeToolCall = true;
                                    hasEmittedToolCall = true;
                                    string arguments = JsonSerializer.Serialize(toolCall.Input, _jsonOptions);
                                    emittedText.Append(toolCall.Name).Append(arguments);
                                    await WriteChunkAsync(new ChatChunkDeltaDto
                                    {
                                        ToolCalls = new List<ToolCallDto>
                                        {
                                            new()
                                            {
                                                Index = nextToolIndex++,
                                                Id = toolCall.Id,
                                                Function = new FunctionCallDto { Name = toolCall.Name, Arguments = arguments }
                                            }
                                        }
                                    });
                                }
                            }
                            hasStreamEvent = await MoveNextWithCancellationAsync(streamEnumerator, ct);
                        }

                        foreach (var parsedChunk in reasoningParser.Flush())
                        {
                            string text = RemoveTrailingMarkupPrefix(parsedChunk.Text);
                            if (text.Length > 0)
                            {
                                await ProcessReasoningChunkAsync(new ReasoningParsedChunk(parsedChunk.Kind, text));
                            }
                        }

                        if (toolsDeclared)
                        {
                            foreach (var parsedEvent in toolParser.Flush())
                            {
                                if (parsedEvent.Type == ToolParsedEventType.ContentDelta && parsedEvent.Content != null)
                                {
                                    parsedEvent.Content = RemoveTrailingMarkupPrefix(parsedEvent.Content);
                                }
                                await EmitToolEventAsync(parsedEvent);
                            }
                        }

                        string finishReason = hasEmittedToolCall ? "tool_calls" : "stop";
                        await WriteChunkAsync(new ChatChunkDeltaDto(), finishReason);

                        if (request.StreamOptions?.IncludeUsage == true)
                        {
                            var usageChunk = new ChatCompletionChunk
                            {
                                Id = completionId,
                                Model = resolvedModel,
                                Choices = new List<ChatChunkChoiceDto>(),
                                Usage = new CompletionUsageDto
                                {
                                    PromptTokens = promptTokens,
                                    CompletionTokens = provider.TokenCounter.CountTokens(emittedText.ToString())
                                }
                            };
                            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(usageChunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                        }

                        await context.Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8, ct);
                        await context.Response.Body.FlushAsync(ct);

                        try
                        {
                            sw.Stop();
                            int compTokens = provider.TokenCounter.CountTokens(emittedText.ToString());
                            _ = Claude4Net.Runtime.Telemetry.TeruTeruPandasTelemetryEngine.Shared.RecordTokenUsageAsync(
                                sessionId: AppState.SessionId ?? "api-gateway",
                                projectName: "Claude4Net-ApiGateway",
                                provider: provider.Name,
                                model: resolvedModel,
                                promptTokens: promptTokens,
                                compTokens: compTokens,
                                latencyMs: sw.Elapsed.TotalMilliseconds
                            );
                        }
                        catch { }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }

                    return Results.Empty;
                    }
                    finally
                    {
                        await DisposeEnumeratorAsync(streamEnumerator, ct);
                    }
                }
                else
                {
                    try
                    {
                        var textResponse = new StringBuilder();
                        var reasoningResponse = new StringBuilder();
                        var nativeToolCalls = new List<ToolCallDto>();
                        var reasoningParser = new IncrementalReasoningParser();
                        var ct = context.RequestAborted;
                        var streamEnumerator = provider.StreamQueryAsync(prompt, model: resolvedModel, ct: ct).GetAsyncEnumerator(ct);
                        try
                        {
                            while (await MoveNextWithCancellationAsync(streamEnumerator, ct))
                            {
                                var streamEvent = streamEnumerator.Current;
                                if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                                {
                                    foreach (var parsedChunk in reasoningParser.ProcessChunk(streamEvent.Delta))
                                    {
                                        if (parsedChunk.Kind == ReasoningChunkKind.Reasoning)
                                        {
                                            reasoningResponse.Append(parsedChunk.Text);
                                        }
                                        else
                                        {
                                            textResponse.Append(parsedChunk.Text);
                                        }
                                    }
                                }
                                else if (streamEvent.Type == LLMStreamEventType.ThinkingDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                                {
                                    reasoningResponse.Append(streamEvent.Delta);
                                }
                                else if (streamEvent.Type == LLMStreamEventType.ToolCallStart && streamEvent.ToolCall != null)
                                {
                                    var toolCall = streamEvent.ToolCall;
                                    if (!IsDeclaredToolCall(toolCall.Name, declaredToolNames)) continue;
                                    nativeToolCalls.Add(new ToolCallDto
                                    {
                                        Index = nativeToolCalls.Count,
                                        Id = toolCall.Id,
                                        Function = new FunctionCallDto
                                        {
                                            Name = toolCall.Name,
                                            Arguments = JsonSerializer.Serialize(toolCall.Input, _jsonOptions)
                                        }
                                    });
                                }
                            }

                            foreach (var parsedChunk in reasoningParser.Flush())
                            {
                                if (parsedChunk.Kind == ReasoningChunkKind.Reasoning)
                                {
                                    reasoningResponse.Append(parsedChunk.Text);
                                }
                                else
                                {
                                    textResponse.Append(parsedChunk.Text);
                                }
                            }

                            string responseText = StopSequenceHelper.Apply(textResponse.ToString(), request.Stop);
                            responseText = ApplyTokenLimit(responseText, request.EffectiveMaxTokens, provider.TokenCounter, out bool wasTruncated);
                            int promptTokens = provider.TokenCounter.CountTokens(prompt);
                            int completionTokens = provider.TokenCounter.CountTokens(responseText);

                            var toolCalls = nativeToolCalls.Count > 0
                                ? nativeToolCalls
                                : request.Tools != null && request.Tools.Count > 0
                                    ? ParseToolCallsFromText(responseText)?
                                        .Where(call => IsDeclaredToolCall(call.Function.Name, declaredToolNames))
                                        .Select((call, index) =>
                                        {
                                            call.Index = index;
                                            return call;
                                        })
                                        .ToList()
                                    : null;
                            bool hasTools = toolCalls != null && toolCalls.Count > 0;
                            if (!hasTools && request.Tools != null && request.Tools.Count > 0)
                            {
                                responseText = StripToolMarkup(responseText);
                            }

                            var response = new ChatCompletionResponse
                            {
                                Id = completionId,
                                Model = resolvedModel,
                                Choices = new List<ChatChoiceDto>
                                {
                                    new()
                                    {
                                        Index = 0,
                                        Message = new ChatMessageDto
                                        {
                                            Role = "assistant",
                                            Content = hasTools ? null : responseText,
                                            ReasoningContent = reasoningResponse.Length > 0 ? reasoningResponse.ToString() : null,
                                            ToolCalls = hasTools ? toolCalls : null
                                        },
                                        FinishReason = hasTools ? "tool_calls" : wasTruncated ? "length" : "stop"
                                    }
                                },
                                Usage = new CompletionUsageDto
                                {
                                    PromptTokens = promptTokens,
                                    CompletionTokens = completionTokens
                                }
                            };

                            try
                            {
                                sw.Stop();
                                _ = Claude4Net.Runtime.Telemetry.TeruTeruPandasTelemetryEngine.Shared.RecordTokenUsageAsync(
                                    sessionId: AppState.SessionId ?? "api-gateway",
                                    projectName: "Claude4Net-ApiGateway",
                                    provider: provider.Name,
                                    model: resolvedModel,
                                    promptTokens: promptTokens,
                                    compTokens: completionTokens,
                                    latencyMs: sw.Elapsed.TotalMilliseconds
                                );
                            }
                            catch { }

                            return Results.Json(response, _jsonOptions);
                        }
                        finally
                        {
                            await DisposeEnumeratorAsync(streamEnumerator, ct);
                        }
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        return ProviderError(ex);
                    }
                }
            };
            app.MapPost("/v1/chat/completions", handleChatCompletions);
            app.MapPost("/api/v1/chat/completions", handleChatCompletions);
        }

        private void MapCustomEndpoints(WebApplication app)
        {
            // GET /api/v1/health & /api/v1/status
            app.MapGet("/api/v1/health", () => Results.Ok(new { status = "healthy" }));
            app.MapGet("/api/v1/status", () => Results.Ok(GetStatus()));

            // Discovery & Compatibility Endpoints (Hermes / Ollama / llama.cpp discovery probes)
            app.MapGet("/version", () => Results.Ok(new { version = "0.5.0" }));
            app.MapGet("/api/tags", () => Results.Ok(new
            {
                models = Catalog.Models.Select(m => new
                {
                    name = m.Id,
                    model = m.Id,
                    modified_at = DateTime.UtcNow.ToString("o"),
                    size = 0,
                    digest = "sha256:0000000000000000000000000000000000000000000000000000000000000000",
                    details = new { format = "gguf", family = "custom", parameter_size = "N/A", quantization_level = "N/A" }
                }).ToList()
            }));
            app.MapPost("/api/show", () => Results.Ok(new
            {
                modelfile = "# Claude4Net OpenAI Compatible Proxy\nFROM claude4net",
                parameters = "",
                template = "{{ .Prompt }}"
            }));
            Delegate handleProps = () => Results.Ok(new
            {
                default_generation_settings = new { },
                total_slots = 1
            });
            app.MapGet("/props", handleProps);
            app.MapGet("/v1/props", handleProps);
            app.MapGet("/favicon.ico", () => Results.NoContent());

            // GET /api/v1/usage
            app.MapGet("/api/v1/usage", async () =>
            {
                var usage = await GetUsageAsync();
                return Results.Ok(usage);
            });

            // POST /api/v1/agent/run
            app.MapPost("/api/v1/agent/run", () => Results.Json(
                new OpenAiErrorEnvelope(new OpenAiError(
                    "Agent execution is disabled for this API server.",
                    "permission_error",
                    "agent_run_disabled")),
                _jsonOptions,
                statusCode: StatusCodes.Status403Forbidden));

            // GET /api/v1/tools
            app.MapGet("/api/v1/tools", () =>
            {
                var tools = _serviceProvider.GetServices<ITool>().Select(t => new ToolItemDto
                {
                    Name = t.Name,
                    Description = t.Description,
                    ReadOnly = false
                }).ToList();

                return Results.Ok(new { count = tools.Count, tools });
            });

            // GET /api/v1/skills
            app.MapGet("/api/v1/skills", async () =>
            {
                var skillRegistry = _serviceProvider.GetService<SkillRegistryService>();
                var skills = new List<SkillItemDto>();

                if (skillRegistry != null)
                {
                    await skillRegistry.LoadAsync();
                    foreach (var s in skillRegistry.ListSkills())
                    {
                        skills.Add(new SkillItemDto
                        {
                            Name = s.Id,
                            Description = s.Description,
                            Path = s.SourcePath
                        });
                    }
                }

                return Results.Ok(new { count = skills.Count, skills });
            });
        }

        private ApiStatusResponse GetStatus()
        {
            return new ApiStatusResponse
            {
                Status = "healthy",
                Port = Port,
                ActiveProvider = AppState.ActiveProvider,
                ActiveModel = AppState.ActiveModel,
                PermissionMode = AppState.CurrentPermissionMode.ToString(),
                Workspace = AppState.CurrentCwd ?? Environment.CurrentDirectory,
                UptimeSeconds = Math.Round((DateTime.UtcNow - _startTime).TotalSeconds, 1)
            };
        }

        private async Task<ApiUsageResponse> GetUsageAsync()
        {
            string sessionId = AppState.SessionId;
            string ws = AppState.CurrentCwd ?? AppState.OriginalCwd ?? AppState.SystemBaseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            var eventStore = new FileAgentEventStore(ws);
            var projectionEngine = new EventProjectionEngine(eventStore);
            var usageProjection = new UsageProjection();
            projectionEngine.RegisterProjection(usageProjection);

            try
            {
                await projectionEngine.RebuildAsync(sessionId);
            }
            catch { }

            var model = usageProjection.Model;
            var (activeProvider, _) = ResolveProviderAndModel(null);
            int limit = activeProvider?.ContextLimit ?? 200000;
            int historyTokens = 0;

            if (activeProvider != null)
            {
                var history = activeProvider.GetHistory();
                historyTokens = activeProvider.TokenCounter.CountTokens(history);
            }

            var components = new Dictionary<string, int>
            {
                { "ConversationHistory", historyTokens },
                { "EstimatedSystemPrompt", 3500 },
                { "ToolDefinitions", 3200 }
            };

            int currentTotal = historyTokens + 3500 + 3200;

            return new ApiUsageResponse
            {
                SessionId = sessionId,
                TotalCalls = model.TotalCalls,
                InputTokens = model.TotalInputTokens,
                OutputTokens = model.TotalOutputTokens,
                TotalCost = model.TotalCost,
                ContextLimit = limit,
                CurrentContextTokens = currentTotal,
                ContextComponents = components
            };
        }

        private (ILLMProvider? Provider, string Model) ResolveProviderAndModel(string? requestedModel, string? requestedProvider = null)
        {
            if (_providerRegistry == null) return (null, requestedModel ?? "default");

            string targetProvider = !string.IsNullOrEmpty(requestedProvider) ? requestedProvider : AppState.ActiveProvider;
            string model = string.IsNullOrWhiteSpace(requestedModel) ? AppState.ActiveModel : requestedModel;

            if (!string.IsNullOrEmpty(requestedProvider))
            {
                try
                {
                    var p = _providerRegistry.CreateProvider(requestedProvider, _serviceProvider);
                    return (p, model);
                }
                catch
                {
                    return (null, model);
                }
            }

            // Route based on model prefix
            if (model.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                targetProvider = "claude";
            else if (model.StartsWith("gemini", StringComparison.OrdinalIgnoreCase))
                targetProvider = "gemini";
            else if (model.StartsWith("glm", StringComparison.OrdinalIgnoreCase))
                targetProvider = "glm";
            else if (model.Contains("llama", StringComparison.OrdinalIgnoreCase) || model.Contains("qwen", StringComparison.OrdinalIgnoreCase) || model.Contains("ollama", StringComparison.OrdinalIgnoreCase))
                targetProvider = "ollama";

            try
            {
                var p = _providerRegistry.CreateProvider(targetProvider, _serviceProvider);
                return (p, model);
            }
            catch
            {
                return (null, model);
            }
        }

        private (RequestProviderLease? Lease, string Model) ResolveRequestProviderAndModel(string? requestedModel)
        {
            if (_providerRegistry == null) return (null, requestedModel ?? "default");

            string model = requestedModel ?? string.Empty;
            if (Catalog.TryGetChatRoute(model, out ChatModelRoute route))
            {
                try
                {
                    var lease = _providerRegistry.CreateRequestProviderLease(route.ProviderId, _serviceProvider);
                    return (lease, model);
                }
                catch
                {
                    // Fallthrough to fallback provider
                }
            }

            // Fallback: If route not found or lease failed, use active provider (or antigravity-cli)
            try
            {
                string fallbackId = !string.IsNullOrEmpty(AppState.ActiveProvider) ? AppState.ActiveProvider : "antigravity-cli";
                var lease = _providerRegistry.CreateRequestProviderLease(fallbackId, _serviceProvider);
                return (lease, model);
            }
            catch
            {
                return (null, model);
            }
        }

        private ApiModelCatalog Catalog => _apiModelCatalog ??
            throw new InvalidOperationException("The API model catalog has not been initialized.");

        private static IResult InvalidModelError()
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    message = "The model field is required and cannot be blank.",
                    type = "invalid_request_error",
                    param = "model",
                    code = "invalid_model"
                }
            });
        }

        private static IResult InvalidRequest(OpenAiRequestValidationError error) =>
            error.Parameter == "model" ? InvalidModelError() : InvalidOption(error.Parameter, error.Message);

        private static bool IsDeclaredToolCall(string? toolName, IReadOnlySet<string> declaredToolNames) =>
            toolName is not null && declaredToolNames.Contains(toolName);

        private static IResult ModelNotFoundError(string modelId)
        {
            return Results.BadRequest(new
            {
                error = new
                {
                    message = $"The model '{modelId}' does not exist or is not available for this endpoint.",
                    type = "invalid_request_error",
                    param = "model",
                    code = "model_not_found"
                }
            });
        }

        private static IResult? ValidateKnownOptions(ChatCompletionRequest request)
        {
            if (request.Temperature.HasValue && (request.Temperature.Value < 0.0 || request.Temperature.Value > 2.0))
                return InvalidOption("temperature", "temperature must be between 0.0 and 2.0.");
            if (request.TopP.HasValue && (request.TopP.Value < 0.0 || request.TopP.Value > 1.0))
                return InvalidOption("top_p", "top_p must be between 0.0 and 1.0.");
            if (request.PresencePenalty.HasValue && (request.PresencePenalty.Value < -2.0 || request.PresencePenalty.Value > 2.0))
                return InvalidOption("presence_penalty", "presence_penalty must be between -2.0 and 2.0.");
            if (request.FrequencyPenalty.HasValue && (request.FrequencyPenalty.Value < -2.0 || request.FrequencyPenalty.Value > 2.0))
                return InvalidOption("frequency_penalty", "frequency_penalty must be between -2.0 and 2.0.");
            if (!IsAutoToolChoice(request.ToolChoice))
                return UnsupportedOption("tool_choice");
            if (!IsValidTokenLimit(request.MaxTokens))
                return InvalidOption("max_tokens", "max_tokens must be positive.");
            if (!IsValidTokenLimit(request.MaxCompletionTokens))
                return InvalidOption("max_completion_tokens", "max_completion_tokens must be positive.");

            if (request.ResponseFormat != null)
            {
                string type = request.ResponseFormat.Type;
                if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "json_object", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!string.Equals(type, "json_schema", StringComparison.OrdinalIgnoreCase) || !HasJsonSchema(request.ResponseFormat.JsonSchema))
                {
                    return InvalidOption("response_format", "response_format is not supported or is missing a schema.");
                }
            }

            return null;
        }

        private static IResult? ValidateKnownOptions(TextCompletionRequest request)
        {
            if (request.Temperature.HasValue && (request.Temperature.Value < 0.0 || request.Temperature.Value > 2.0))
                return InvalidOption("temperature", "temperature must be between 0.0 and 2.0.");
            if (!IsValidTokenLimit(request.MaxTokens))
                return InvalidOption("max_tokens", "max_tokens must be positive.");
            return null;
        }

        private static bool IsValidTokenLimit(int? value) => !value.HasValue || value.Value >= 1;

        private static bool IsAutoToolChoice(object? toolChoice)
        {
            if (toolChoice == null) return true;
            if (toolChoice is string value) return string.Equals(value, "auto", StringComparison.Ordinal);
            return toolChoice is JsonElement element &&
                element.ValueKind == JsonValueKind.String &&
                string.Equals(element.GetString(), "auto", StringComparison.Ordinal);
        }

        private static bool HasJsonSchema(object? schema)
        {
            if (schema == null) return false;
            return schema is not JsonElement element || element.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
        }

        private static IResult UnsupportedOption(string param) =>
            InvalidOption(param, $"The supplied {param} value is not supported.");

        private static IResult InvalidOption(string param, string message) => Results.BadRequest(new
        {
            error = new
            {
                message,
                type = "invalid_request_error",
                param,
                code = "invalid_value"
            }
        });

        private static IResult InvalidJsonError() => Results.BadRequest(new
        {
            error = new
            {
                message = "Invalid JSON payload.",
                type = "invalid_request_error",
                code = "invalid_json"
            }
        });

        private static IResult ProviderError(Exception? ex = null)
        {
            if (ex != null)
            {
                Console.WriteLine($"[API Server] Upstream provider error: {ex.Message}");
            }
            return Results.Json(new
            {
                error = new
                {
                    message = "The upstream provider request failed.",
                    type = "provider_error",
                    code = "provider_error"
                }
            }, _jsonOptions, statusCode: StatusCodes.Status502BadGateway);
        }

        private static TextCompletionResponse CreateTextCompletionResponse(string id, string model, string text, string? finishReason)
        {
            return new TextCompletionResponse
            {
                Id = id,
                Model = model,
                Choices = new List<TextChoiceDto>
                {
                    new() { Text = text, Index = 0, FinishReason = finishReason }
                }
            };
        }

        private static async Task WriteSseAsync(HttpContext context, TextCompletionResponse response, CancellationToken ct)
        {
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(response, _jsonOptions)}\n\n", Encoding.UTF8, ct);
            await context.Response.Body.FlushAsync(ct);
        }

        private static Task<bool> MoveNextWithCancellationAsync(
            IAsyncEnumerator<LLMStreamEvent> enumerator,
            CancellationToken ct)
            => enumerator.MoveNextAsync().AsTask().WaitAsync(ct);

        private static async Task DisposeEnumeratorAsync(
            IAsyncEnumerator<LLMStreamEvent> enumerator,
            CancellationToken ct)
        {
            try
            {
                await enumerator.DisposeAsync().AsTask().WaitAsync(ct);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
            }
        }

        private static string ApplyTokenLimit(string text, int? maxTokens, ITokenCounter tokenCounter, out bool wasTruncated)
        {
            wasTruncated = false;
            if (!maxTokens.HasValue || tokenCounter.CountTokens(text) <= maxTokens.Value)
            {
                return text;
            }

            int low = 0;
            int high = text.Length;
            while (high - low > 1)
            {
                int candidateLength = low + (high - low) / 2;
                if (char.IsHighSurrogate(text[candidateLength - 1]) &&
                    char.IsLowSurrogate(text[candidateLength]))
                {
                    candidateLength--;
                }

                if (candidateLength == low)
                {
                    candidateLength++;
                    if (candidateLength < high &&
                        char.IsHighSurrogate(text[candidateLength - 1]) &&
                        char.IsLowSurrogate(text[candidateLength]))
                    {
                        candidateLength++;
                    }
                    if (candidateLength >= high) break;
                }

                if (tokenCounter.CountTokens(text[..candidateLength]) <= maxTokens.Value)
                {
                    low = candidateLength;
                }
                else
                {
                    high = candidateLength;
                }
            }

            wasTruncated = low < text.Length;
            return text[..low];
        }

        private static string StripToolMarkup(string text)
        {
            string withoutCalls = Regex.Replace(
                text,
                @"<invoke\b[^>]*>[\s\S]*?<\/invoke>",
                string.Empty,
                RegexOptions.IgnoreCase);
            return Regex.Replace(withoutCalls, @"<\/?(?:invoke|parameter)\b[^>]*>", string.Empty, RegexOptions.IgnoreCase);
        }

        private static string BuildPromptFromMessages(List<ChatMessageDto> messages, List<ToolDto>? tools = null)
        {
            var sb = new StringBuilder();
            if (tools != null && tools.Count > 0)
            {
                sb.AppendLine("[SYSTEM]: You have access to the following tools:");
                foreach (var tool in tools)
                {
                    sb.AppendLine($"- Tool: {tool.Function.Name}");
                    if (!string.IsNullOrEmpty(tool.Function.Description))
                        sb.AppendLine($"  Description: {tool.Function.Description}");
                    if (tool.Function.Parameters != null)
                        sb.AppendLine($"  Parameters: {JsonSerializer.Serialize(tool.Function.Parameters, _jsonOptions)}");
                }
                sb.AppendLine("To invoke a tool, output: <invoke name=\"tool_name\"><parameter name=\"param_name\">value</parameter></invoke>\n");
            }

            if (messages.Count == 1 && (tools == null || tools.Count == 0))
            {
                return messages[0].GetContentString();
            }

            foreach (var msg in messages)
            {
                sb.AppendLine($"[{msg.Role.ToUpperInvariant()}]: {msg.GetContentString()}");
            }
            return sb.ToString();
        }

        private static List<ToolCallDto>? ParseToolCallsFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var list = new List<ToolCallDto>();

            // Format 1: XML invoke tags: <invoke name="func_name"><parameter name="arg">val</parameter></invoke>
            var invokeMatches = Regex.Matches(
                text, @"<invoke\s+name=[""'](?<name>[^""']+)[""']\s*>(?<content>[\s\S]*?)<\/invoke>",
                RegexOptions.IgnoreCase);

            if (invokeMatches.Count > 0)
            {
                int idx = 0;
                foreach (Match match in invokeMatches)
                {
                    string funcName = match.Groups["name"].Value;
                    string inner = match.Groups["content"].Value;

                    var argsDict = new Dictionary<string, string>();
                    var paramMatches = Regex.Matches(
                        inner, @"<parameter\s+name=[""'](?<pname>[^""']+)[""']\s*>(?<pval>[\s\S]*?)<\/parameter>",
                        RegexOptions.IgnoreCase);

                    foreach (Match pm in paramMatches)
                    {
                        argsDict[pm.Groups["pname"].Value] = pm.Groups["pval"].Value.Trim();
                    }

                    string argsJson = argsDict.Count > 0 ? JsonSerializer.Serialize(argsDict) : "{}";
                    list.Add(new ToolCallDto
                    {
                        Index = idx++,
                        Id = "call_" + Guid.NewGuid().ToString("N")[..12],
                        Type = "function",
                        Function = new FunctionCallDto
                        {
                            Name = funcName,
                            Arguments = argsJson
                        }
                    });
                }
                return list;
            }

            // Format 2: JSON tool invocation: ```json { "name": "...", "arguments": { ... } } ```
            try
            {
                string trimmed = text.Trim();
                if (trimmed.StartsWith("```json") && trimmed.EndsWith("```"))
                {
                    trimmed = trimmed.Substring(7, trimmed.Length - 10).Trim();
                }

                if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("name", out var n) && (root.TryGetProperty("arguments", out var a) || root.TryGetProperty("parameters", out a)))
                    {
                        list.Add(new ToolCallDto
                        {
                            Index = 0,
                            Id = "call_" + Guid.NewGuid().ToString("N")[..12],
                            Type = "function",
                            Function = new FunctionCallDto
                            {
                                Name = n.GetString() ?? "",
                                Arguments = a.ToString()
                            }
                        });
                        return list;
                    }
                }
            }
            catch { }

            return list.Count > 0 ? list : null;
        }

        #region Utility Wrappers (for backward compatibility)

        /// <summary>
        /// Wrapper for EmbeddingUtils.FloatsToBase64
        /// </summary>
        public static string FloatsToBase64(IList<float> floats) => EmbeddingUtils.FloatsToBase64(floats);

        /// <summary>
        /// Wrapper for EmbeddingUtils.Base64ToFloats
        /// </summary>
        public static float[] Base64ToFloats(string base64) => EmbeddingUtils.Base64ToFloats(base64);

        /// <summary>
        /// Wrapper for StopSequenceHelper.Apply
        /// </summary>
        public static string ApplyStopSequences(string text, object? stop) => StopSequenceHelper.Apply(text, stop);

        #endregion

        private sealed record OpenAiErrorEnvelope(OpenAiError Error);

        private sealed record OpenAiError(string Message, string Type, string Code);
    }

}
