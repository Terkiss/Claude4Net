using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Api;
using Claude4Net.Runtime.ApiServer.Models;
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
    public class Claude4NetApiServer
    {
        private IHost? _host;
        private readonly IServiceProvider _serviceProvider;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public const int DefaultPort = 7836;
        public int Port { get; private set; } = DefaultPort;
        public string ApiKey { get; private set; } = string.Empty;
        public bool IsRunning => _host != null;
        public string Url => $"http://localhost:{Port}";

        public Claude4NetApiServer(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartAsync(int port = DefaultPort, string? apiKey = null, CancellationToken ct = default)
        {
            if (_host != null)
            {
                return; // Already running
            }

            Port = port > 0 ? port : DefaultPort;

            // Generate or set API Key
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                ApiKey = apiKey.Trim();
            }
            else if (string.IsNullOrWhiteSpace(ApiKey))
            {
                ApiKey = "c4n-sk-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
            }

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");

            // Configure CORS
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                });
            });

            var app = builder.Build();
            app.UseCors();

            // Strict Authentication Middleware
            app.Use(async (context, next) =>
            {
                // Allow CORS preflight requests
                if (HttpMethods.IsOptions(context.Request.Method))
                {
                    await next();
                    return;
                }

                // Allow unauthenticated health check
                string path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
                if (path == "/api/v1/health" || path == "/health")
                {
                    await next();
                    return;
                }

                // Verify Bearer Token or x-api-key
                if (!string.IsNullOrEmpty(ApiKey))
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    string token = "";
                    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = authHeader.Substring("Bearer ".Length).Trim();
                    }
                    else if (context.Request.Headers.TryGetValue("x-api-key", out var xKey))
                    {
                        token = xKey.ToString().Trim();
                    }

                    if (string.IsNullOrEmpty(token) || !string.Equals(token, ApiKey, StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        var error = new
                        {
                            error = new
                            {
                                message = "Invalid or missing API key. Provide header 'Authorization: Bearer <key>' or 'x-api-key: <key>'.",
                                type = "authentication_error",
                                code = "invalid_api_key"
                            }
                        };
                        await context.Response.WriteAsync(JsonSerializer.Serialize(error, _jsonOptions));
                        return;
                    }
                }

                await next();
            });

            // Register Endpoints
            MapOpenAiEndpoints(app);
            MapCustomEndpoints(app);

            _host = app;
            await _host.StartAsync(ct);
        }

        public async Task StopAsync(CancellationToken ct = default)
        {
            if (_host != null)
            {
                await _host.StopAsync(ct);
                _host.Dispose();
                _host = null;
            }
        }

        private void MapOpenAiEndpoints(WebApplication app)
        {
            // GET /v1/models
            app.MapGet("/v1/models", () =>
            {
                var models = GetRegisteredModelCards();
                return Results.Ok(new ModelListResponse { Data = models });
            });

            // GET /v1/models/{modelId}
            app.MapGet("/v1/models/{modelId}", (string modelId) =>
            {
                var models = GetRegisteredModelCards();
                var found = models.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
                if (found != null)
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
            });

            // POST /v1/completions (Legacy text completion endpoint)
            app.MapPost("/v1/completions", async (HttpContext context) =>
            {
                TextCompletionRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<TextCompletionRequest>(context.Request.Body, _jsonOptions);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = new { message = $"Invalid JSON payload: {ex.Message}", type = "invalid_request_error" } });
                }

                if (request == null || string.IsNullOrWhiteSpace(request.GetPromptString()))
                {
                    return Results.BadRequest(new { error = new { message = "Prompt field cannot be empty.", type = "invalid_request_error" } });
                }

                var (provider, resolvedModel) = ResolveProviderAndModel(request.Model);
                if (provider == null)
                {
                    return Results.BadRequest(new { error = new { message = $"No active or suitable LLM Provider found for model '{request.Model}'.", type = "provider_error" } });
                }

                string prompt = request.GetPromptString();
                string completionId = "cmpl-" + Guid.NewGuid().ToString("N")[..12];

                var sbResponse = new StringBuilder();
                await foreach (var streamEvent in provider.StreamQueryAsync(prompt, model: resolvedModel, ct: context.RequestAborted))
                {
                    if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                    {
                        sbResponse.Append(streamEvent.Delta);
                    }
                }

                string responseText = sbResponse.ToString();
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
                            FinishReason = "stop"
                        }
                    },
                    Usage = new CompletionUsageDto
                    {
                        PromptTokens = promptTokens,
                        CompletionTokens = completionTokens
                    }
                };

                return Results.Json(response, _jsonOptions);
            });

            // POST /v1/embeddings
            app.MapPost("/v1/embeddings", async (HttpContext context) =>
            {
                EmbeddingRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<EmbeddingRequest>(context.Request.Body, _jsonOptions);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = new { message = $"Invalid JSON payload: {ex.Message}", type = "invalid_request_error" } });
                }

                if (request == null)
                {
                    return Results.BadRequest(new { error = new { message = "Embedding payload cannot be empty.", type = "invalid_request_error" } });
                }

                var inputs = request.GetInputs();
                if (inputs.Count == 0)
                {
                    return Results.BadRequest(new { error = new { message = "Input field cannot be empty.", type = "invalid_request_error" } });
                }

                int dimensions = request.Dimensions.HasValue && request.Dimensions.Value > 0 ? request.Dimensions.Value : 1536;
                var response = new EmbeddingResponse
                {
                    Model = string.IsNullOrWhiteSpace(request.Model) ? "text-embedding-004" : request.Model,
                    Data = new List<EmbeddingData>()
                };

                int totalTokens = 0;
                for (int i = 0; i < inputs.Count; i++)
                {
                    string text = inputs[i];
                    totalTokens += Math.Max(1, text.Length / 4);
                    var vector = GenerateDeterministicEmbedding(text, dimensions);
                    response.Data.Add(new EmbeddingData
                    {
                        Index = i,
                        Embedding = vector
                    });
                }

                response.Usage = new EmbeddingUsage
                {
                    PromptTokens = totalTokens
                };

                return Results.Json(response, _jsonOptions);
            });

            // POST /v1/chat/completions
            app.MapPost("/v1/chat/completions", async (HttpContext context) =>
            {
                ChatCompletionRequest? request;
                try
                {
                    request = await JsonSerializer.DeserializeAsync<ChatCompletionRequest>(context.Request.Body, _jsonOptions);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = new { message = $"Invalid JSON payload: {ex.Message}", type = "invalid_request_error" } });
                }

                if (request == null || request.Messages.Count == 0)
                {
                    return Results.BadRequest(new { error = new { message = "Messages array cannot be empty.", type = "invalid_request_error" } });
                }

                var (provider, resolvedModel) = ResolveProviderAndModel(request.Model);
                if (provider == null)
                {
                    return Results.BadRequest(new { error = new { message = $"No active or suitable LLM Provider found for model '{request.Model}'.", type = "provider_error" } });
                }

                // Build prompt / conversation context (including tools instructions if requested)
                string prompt = BuildPromptFromMessages(request.Messages, request.Tools);
                string completionId = "chatcmpl-" + Guid.NewGuid().ToString("N")[..12];

                if (request.Stream)
                {
                    context.Response.ContentType = "text/event-stream; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache";
                    context.Response.Headers.Connection = "keep-alive";

                    var ct = context.RequestAborted;
                    int promptTokens = provider.TokenCounter.CountTokens(prompt);
                    int completionTokens = 0;

                    // Initial role chunk
                    var initialChunk = new ChatCompletionChunk
                    {
                        Id = completionId,
                        Model = resolvedModel,
                        Choices = new List<ChatChunkChoiceDto>
                        {
                            new() { Index = 0, Delta = new ChatChunkDeltaDto { Role = "assistant" } }
                        }
                    };
                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(initialChunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                    await context.Response.Body.FlushAsync(ct);

                    var sbFullResponse = new StringBuilder();
                    bool insideThink = false;

                    try
                    {
                        await foreach (var streamEvent in provider.StreamQueryAsync(prompt, model: resolvedModel, ct: ct))
                        {
                            if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                string delta = streamEvent.Delta;
                                sbFullResponse.Append(delta);
                                completionTokens += provider.TokenCounter.CountTokens(delta);

                                string? reasoningChunk = null;
                                string? contentChunk = null;

                                if (delta.Contains("<think>"))
                                {
                                    insideThink = true;
                                    delta = delta.Replace("<think>", "");
                                }

                                if (insideThink)
                                {
                                    if (delta.Contains("</think>"))
                                    {
                                        var parts = delta.Split("</think>", 2);
                                        reasoningChunk = parts[0];
                                        contentChunk = parts.Length > 1 ? parts[1] : null;
                                        insideThink = false;
                                    }
                                    else
                                    {
                                        reasoningChunk = delta;
                                    }
                                }
                                else
                                {
                                    contentChunk = delta;
                                }

                                if (!string.IsNullOrEmpty(reasoningChunk) || !string.IsNullOrEmpty(contentChunk))
                                {
                                    var chunk = new ChatCompletionChunk
                                    {
                                        Id = completionId,
                                        Model = resolvedModel,
                                        Choices = new List<ChatChunkChoiceDto>
                                        {
                                            new()
                                            {
                                                Index = 0,
                                                Delta = new ChatChunkDeltaDto
                                                {
                                                    Content = contentChunk,
                                                    ReasoningContent = reasoningChunk
                                                }
                                            }
                                        }
                                    };
                                    await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(chunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                                    await context.Response.Body.FlushAsync(ct);
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { }

                    // Check if tools were invoked in streamed output
                    string fullText = sbFullResponse.ToString();
                    var toolCalls = request.Tools != null && request.Tools.Count > 0 ? ParseToolCallsFromText(fullText) : null;
                    string finishReason = toolCalls != null && toolCalls.Count > 0 ? "tool_calls" : "stop";

                    if (toolCalls != null && toolCalls.Count > 0)
                    {
                        var toolCallChunk = new ChatCompletionChunk
                        {
                            Id = completionId,
                            Model = resolvedModel,
                            Choices = new List<ChatChunkChoiceDto>
                            {
                                new() { Index = 0, Delta = new ChatChunkDeltaDto { ToolCalls = toolCalls }, FinishReason = finishReason }
                            }
                        };
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(toolCallChunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                    }
                    else
                    {
                        var finalChunk = new ChatCompletionChunk
                        {
                            Id = completionId,
                            Model = resolvedModel,
                            Choices = new List<ChatChunkChoiceDto>
                            {
                                new() { Index = 0, Delta = new ChatChunkDeltaDto(), FinishReason = finishReason }
                            }
                        };
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(finalChunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                    }

                    // If client requested stream_options: { include_usage: true }, emit final usage chunk
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
                                CompletionTokens = completionTokens
                            }
                        };
                        await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(usageChunk, _jsonOptions)}\n\n", Encoding.UTF8, ct);
                    }

                    await context.Response.WriteAsync("data: [DONE]\n\n", Encoding.UTF8, ct);
                    await context.Response.Body.FlushAsync(ct);

                    return Results.Empty;
                }
                else
                {
                    try
                    {
                        var sbResponse = new StringBuilder();
                        await foreach (var streamEvent in provider.StreamQueryAsync(prompt, model: resolvedModel, ct: context.RequestAborted))
                        {
                            if (streamEvent.Type == LLMStreamEventType.TextDelta && !string.IsNullOrEmpty(streamEvent.Delta))
                            {
                                sbResponse.Append(streamEvent.Delta);
                            }
                        }

                        string responseText = sbResponse.ToString();
                        int promptTokens = provider.TokenCounter.CountTokens(prompt);
                        int completionTokens = provider.TokenCounter.CountTokens(responseText);

                        var toolCalls = request.Tools != null && request.Tools.Count > 0 ? ParseToolCallsFromText(responseText) : null;
                        bool hasTools = toolCalls != null && toolCalls.Count > 0;

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
                                        ToolCalls = hasTools ? toolCalls : null
                                    },
                                    FinishReason = hasTools ? "tool_calls" : "stop"
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
                    catch (Exception ex)
                    {
                        return Results.Json(new { error = new { message = ex.Message, type = "api_error" } }, statusCode: 500);
                    }
                }
            });
        }

        private void MapCustomEndpoints(WebApplication app)
        {
            // GET /api/v1/health & /api/v1/status
            app.MapGet("/api/v1/health", () => Results.Ok(GetStatus()));
            app.MapGet("/api/v1/status", () => Results.Ok(GetStatus()));

            // GET /api/v1/usage
            app.MapGet("/api/v1/usage", async () =>
            {
                var usage = await GetUsageAsync();
                return Results.Ok(usage);
            });

            // POST /api/v1/agent/run
            app.MapPost("/api/v1/agent/run", async (AgentRunRequest request, HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return Results.BadRequest(new { error = "Prompt cannot be empty." });
                }

                var agentLoop = _serviceProvider.GetService<AgentLoop>();
                var providerRegistry = _serviceProvider.GetService<ProviderRegistry>();

                if (agentLoop == null || providerRegistry == null)
                {
                    return Results.Json(new { error = "AgentLoop runtime is not configured." }, statusCode: 500);
                }

                var (provider, resolvedModel) = ResolveProviderAndModel(request.Model, request.Provider);
                if (provider == null)
                {
                    return Results.BadRequest(new { error = "Could not resolve an active LLM provider." });
                }

                var outputHandler = new InMemoryOutputHandler();
                var sw = System.Diagnostics.Stopwatch.StartNew();

                await agentLoop.RunAsync(request.Prompt, outputHandler, provider, resolvedModel, approval: null, ct: context.RequestAborted);
                sw.Stop();

                var response = new AgentRunResponse
                {
                    SessionId = AppState.SessionId,
                    Response = outputHandler.GetFullOutput(),
                    Turns = 1,
                    DurationMs = sw.Elapsed.TotalMilliseconds
                };

                return Results.Ok(response);
            });

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
            var registry = _serviceProvider.GetService<ProviderRegistry>();
            if (registry == null) return (null, requestedModel ?? "default");

            string targetProvider = !string.IsNullOrEmpty(requestedProvider) ? requestedProvider : AppState.ActiveProvider;
            string model = string.IsNullOrWhiteSpace(requestedModel) ? AppState.ActiveModel : requestedModel;

            if (!string.IsNullOrEmpty(requestedProvider))
            {
                try
                {
                    var p = registry.CreateProvider(requestedProvider, _serviceProvider);
                    return (p, model);
                }
                catch { }
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
                var p = registry.CreateProvider(targetProvider, _serviceProvider);
                return (p, model);
            }
            catch
            {
                try
                {
                    var p = registry.CreateProvider(AppState.ActiveProvider, _serviceProvider);
                    return (p, model);
                }
                catch
                {
                    return (null, model);
                }
            }
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

        private static List<float> GenerateDeterministicEmbedding(string text, int dimensions = 1536)
        {
            var vector = new float[dimensions];
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);

            int seed = BitConverter.ToInt32(hash, 0);
            var rand = new Random(seed);

            double normSq = 0.0;
            for (int i = 0; i < dimensions; i++)
            {
                double u1 = 1.0 - rand.NextDouble();
                double u2 = 1.0 - rand.NextDouble();
                double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

                int charIndex = bytes.Length > 0 ? i % bytes.Length : 0;
                byte b = bytes.Length > 0 ? bytes[charIndex] : (byte)0;
                float val = (float)(randStdNormal * 0.5 + (b / 255.0 - 0.5));
                vector[i] = val;
                normSq += val * val;
            }

            float norm = (float)Math.Sqrt(normSq);
            if (norm > 0)
            {
                for (int i = 0; i < dimensions; i++)
                {
                    vector[i] /= norm;
                }
            }

            return vector.ToList();
        }

        private static List<ModelCardDto> GetRegisteredModelCards()
        {
            return new List<ModelCardDto>
            {
                new() { Id = "claude-3-7-sonnet-20250219", OwnedBy = "anthropic" },
                new() { Id = "claude-3-5-sonnet-20241022", OwnedBy = "anthropic" },
                new() { Id = "claude-3-5-haiku-20241022", OwnedBy = "anthropic" },
                new() { Id = "gemini-2.5-pro", OwnedBy = "google" },
                new() { Id = "gemini-2.5-flash", OwnedBy = "google" },
                new() { Id = "gemini-2.0-flash", OwnedBy = "google" },
                new() { Id = "text-embedding-004", OwnedBy = "google" },
                new() { Id = "glm-4-plus", OwnedBy = "zhipu" },
                new() { Id = "gpt-4o", OwnedBy = "openai" },
                new() { Id = "gpt-4o-mini", OwnedBy = "openai" },
                new() { Id = "text-embedding-3-small", OwnedBy = "openai" },
                new() { Id = "llama3:latest", OwnedBy = "ollama" },
                new() { Id = "qwen2.5-coder:latest", OwnedBy = "ollama" },
                new() { Id = "claude4net-agent", OwnedBy = "claude4net" }
            };
        }
    }

    /// <summary>
    /// In-Memory output handler for capturing AgentLoop execution results.
    /// </summary>
    internal class InMemoryOutputHandler : IOutputHandler
    {
        private readonly StringBuilder _sb = new();

        public Task WriteAsync(string message)
        {
            _sb.Append(message);
            return Task.CompletedTask;
        }

        public Task WriteLineAsync(string message)
        {
            _sb.AppendLine(message);
            return Task.CompletedTask;
        }

        public Task ShowStatusAsync(string status) => Task.CompletedTask;
        public Task ShowErrorAsync(string error)
        {
            _sb.AppendLine($"[ERROR] {error}");
            return Task.CompletedTask;
        }

        public Task SendFileAsync(string filePath, string? comment = null)
        {
            _sb.AppendLine($"[FILE] {filePath} ({comment ?? ""})");
            return Task.CompletedTask;
        }

        public Task CompleteAsync(string finalResponse)
        {
            if (!string.IsNullOrEmpty(finalResponse) && !_sb.ToString().Contains(finalResponse))
            {
                _sb.Append(finalResponse);
            }
            return Task.CompletedTask;
        }

        public string GetFullOutput() => _sb.ToString();
    }
}
