using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Runtime;
using Claude4Net.Runtime.ApiServer;
using Claude4Net.Runtime.ApiServer.Models;
using Claude4Net.Runtime.Handlers;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public sealed class ApiServerSecurityTests : IDisposable
    {
        private const string TestApiKey = "c4n-security-test-key";
        private const string ProviderId = "security-delay";
        private readonly string _originalActiveProvider = AppState.ActiveProvider;
        private readonly string _originalActiveModel = AppState.ActiveModel;
        private readonly bool _originalIsProviderExplicitlySet = AppState.IsProviderExplicitlySet;

        public void Dispose()
        {
            AppState.ActiveProvider = _originalActiveProvider;
            AppState.ActiveModel = _originalActiveModel;
            AppState.IsProviderExplicitlySet = _originalIsProviderExplicitlySet;
        }

        [Fact]
        public void ServerOptions_Defaults_AreLoopbackAndResourceBound()
        {
            Type optionsType = GetRequiredOptionsType();
            object options = Activator.CreateInstance(optionsType)!;

            Assert.Equal("127.0.0.1", GetRequiredProperty(options, "BindAddress"));
            Assert.False((bool)GetRequiredProperty(options, "AllowRemote"));
            Assert.Equal(1_048_576L, Convert.ToInt64(GetRequiredProperty(options, "MaxRequestBodyBytes")));
            Assert.Equal(TimeSpan.FromMinutes(10), GetRequiredProperty(options, "RequestTimeout"));
            Assert.Equal(16, GetRequiredProperty(options, "MaxConcurrentRequests"));
            Assert.Equal(0, GetRequiredProperty(options, "MaxQueuedRequests"));
            Assert.Null(optionsType.GetProperty("CertificatePassword"));

            MethodInfo? overload = typeof(Claude4NetApiServer).GetMethods()
                .SingleOrDefault(method =>
                    method.Name == nameof(Claude4NetApiServer.StartAsync) &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == optionsType &&
                    method.GetParameters()[1].ParameterType == typeof(CancellationToken));
            Assert.NotNull(overload);
        }

        [Fact]
        public async Task ServerOptions_RemoteWithoutOptIn_IsRejectedBeforeBinding()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            var options = new Claude4NetApiServerOptions
            {
                Port = GetAvailablePort(),
                ApiKey = TestApiKey,
                BindAddress = "192.0.2.10"
            };

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.StartAsync(options));

            Assert.Contains("AllowRemote", error.Message, StringComparison.Ordinal);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task ServerOptions_RemoteOptInWithoutCertificate_IsRejectedBeforeBinding()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            var options = new Claude4NetApiServerOptions
            {
                Port = GetAvailablePort(),
                ApiKey = TestApiKey,
                BindAddress = "192.0.2.10",
                AllowRemote = true
            };

            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => server.StartAsync(options));

            Assert.Contains("certificate", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task ServerOptions_InvalidCertificate_IsRejectedBeforeBinding()
        {
            string certificatePath = Path.GetTempFileName();
            await File.WriteAllTextAsync(certificatePath, "not a certificate");
            try
            {
                await using ServiceProvider services = BuildServices();
                Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
                var options = new Claude4NetApiServerOptions
                {
                    Port = GetAvailablePort(),
                    ApiKey = TestApiKey,
                    BindAddress = "192.0.2.10",
                    AllowRemote = true
                };
                SetRequiredProperty(options, "CertificatePath", certificatePath);

                Exception error = await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync(options));

                Assert.False(error is SocketException);
                Assert.False(server.IsRunning);
            }
            finally
            {
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public void ServerOptions_DoNotExposeEnableAgentRun()
        {
            Type mutableOptionsType = typeof(Claude4NetApiServerOptions);
            Type? validatedOptionsType = mutableOptionsType.Assembly.GetType(
                "Claude4Net.Runtime.ApiServer.ValidatedClaude4NetApiServerOptions");

            Assert.Null(mutableOptionsType.GetProperty("EnableAgentRun"));
            Assert.NotNull(validatedOptionsType);
            Assert.Null(validatedOptionsType!.GetProperty("EnableAgentRun"));
        }

        [Theory]
        [InlineData("0.0.0.0")]
        [InlineData("::")]
        [InlineData("*")]
        public void ServerOptions_WildcardBind_IsRejectedWithoutStartingServer(string wildcard)
        {
            Type optionsType = GetRequiredOptionsType();
            object options = Activator.CreateInstance(optionsType)!;
            PropertyInfo? bindAddress = optionsType.GetProperty("BindAddress");
            Assert.NotNull(bindAddress);

            Exception? error = Record.Exception(() => bindAddress!.SetValue(options, wildcard));

            Assert.NotNull(error);
        }

        [Fact]
        public void CliOptions_AgentRunEnableSwitch_IsNotAccepted()
        {
            CliOptions options = CliOptions.Parse(new[]
            {
                "--api", "on",
                "--api-enable-agent-run"
            });

            Assert.Null(typeof(CliOptions).GetProperty("ApiEnableAgentRun"));
            Assert.True(options.StartApi);
            Assert.Equal(new[] { "--api-enable-agent-run" }, options.RemainingArgs);
            Assert.Null(typeof(Claude4NetApiServerOptions).GetProperty("EnableAgentRun"));
        }

        [Fact]
        public void CliOptions_TlsSwitches_UseEnvironmentVariableNameAndRejectLiteralPassword()
        {
            var options = CliOptions.Parse(new[]
            {
                "--api", "on",
                "--api-certificate", "server-cert.pfx",
                "--api-certificate-password-env", "C4N_TEST_CERT_PASSWORD"
            });

            Assert.Equal("server-cert.pfx", GetRequiredProperty(options, "ApiCertificatePath"));
            Assert.Equal("C4N_TEST_CERT_PASSWORD", GetRequiredProperty(options, "ApiCertificatePasswordEnvironmentVariable"));
            Assert.Null(options.GetType().GetProperty("ApiCertificatePassword"));
            Assert.Empty(options.RemainingArgs);

            CliOptions rejected = CliOptions.Parse(new[]
            {
                "--api", "on",
                "--api-certificate-password", "literal-secret"
            });
            Assert.NotNull(rejected.ValidationError);
            Assert.DoesNotContain("literal-secret", rejected.ValidationError, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiCommand_TlsSwitches_PreservePositionalSyntaxAndKeyCase()
        {
            MethodInfo? parser = typeof(SystemCommands).GetMethod(
                "ParseApiStartOptions",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(parser);

            object? parsedValue = parser!.Invoke(null, new object[]
            {
                new[]
                {
                    "on", "8123", "MiXeD-Key",
                    "--certificate", "server-cert.pfx",
                    "--certificate-password-env", "C4N_TEST_CERT_PASSWORD"
                }
            });
            Assert.NotNull(parsedValue);
            object parsedResult = parsedValue!;
            var parsed = Assert.IsType<Claude4NetApiServerOptions>(GetRequiredProperty(parsedResult, "Options"));

            Assert.Equal(8123, parsed.Port);
            Assert.Equal("MiXeD-Key", parsed.ApiKey);
            Assert.Equal("server-cert.pfx", GetRequiredProperty(parsed, "CertificatePath"));
            Assert.Equal("C4N_TEST_CERT_PASSWORD", GetRequiredProperty(parsed, "CertificatePasswordEnvironmentVariable"));
            Assert.Null(parsed.GetType().GetProperty("CertificatePassword"));

            TargetInvocationException literalPasswordError = Assert.Throws<TargetInvocationException>(() => parser.Invoke(null, new object[]
            {
                new[] { "on", "--certificate-password", "literal-secret" }
            }));
            Assert.IsType<ArgumentException>(literalPasswordError.InnerException);
            Assert.DoesNotContain("literal-secret", literalPasswordError.InnerException!.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ApiCommand_ApiKeyEnvironmentVariable_ResolvesKey()
        {
            string environmentVariable = "C4N_TEST_INTERACTIVE_API_KEY_" + Guid.NewGuid().ToString("N");
            string apiKey = "c4n-interactive-env-" + Guid.NewGuid().ToString("N");
            string? originalValue = Environment.GetEnvironmentVariable(environmentVariable);
            Environment.SetEnvironmentVariable(environmentVariable, apiKey);
            try
            {
                MethodInfo? parser = typeof(SystemCommands).GetMethod(
                    "ParseApiStartOptions",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(parser);

                object? parsedValue = parser!.Invoke(null, new object[]
                {
                    new[] { "start", "--api-key-env", environmentVariable }
                });
                Assert.NotNull(parsedValue);
                object parsedResult = parsedValue!;
                var parsed = Assert.IsType<Claude4NetApiServerOptions>(GetRequiredProperty(parsedResult, "Options"));

                Assert.Equal(apiKey, parsed.ApiKey);
                PropertyInfo? warningProperty = parsedResult.GetType().GetProperty("Warning");
                Assert.NotNull(warningProperty);
                Assert.Null(warningProperty!.GetValue(parsedResult));
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalValue);
            }
        }

        [Fact]
        public void ApiCommand_LiteralApiKey_ReportsNonSecretDeprecationWarning()
        {
            const string expectedWarning = "--api-key is deprecated; use --api-key-env <NAME>.";
            string apiKey = "c4n-interactive-literal-" + Guid.NewGuid().ToString("N");
            MethodInfo? parser = typeof(SystemCommands).GetMethod(
                "ParseApiStartOptions",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(parser);

            object? parsedValue = parser!.Invoke(null, new object[]
            {
                new[] { "start", "--key", apiKey }
            });
            Assert.NotNull(parsedValue);
            object parsedResult = parsedValue!;
            var parsed = Assert.IsType<Claude4NetApiServerOptions>(GetRequiredProperty(parsedResult, "Options"));
            string warning = Assert.IsType<string>(GetRequiredProperty(parsedResult, "Warning"));

            Assert.Equal(apiKey, parsed.ApiKey);
            Assert.Equal(expectedWarning, warning);
            Assert.DoesNotContain(apiKey, warning, StringComparison.Ordinal);
        }

        [Fact]
        public void ServerOptions_RemoteExplicitApiKey_RequiresAtLeast32Characters()
        {
            const string certificatePassword = "test-only-password";
            string environmentVariable = "C4N_TEST_REMOTE_KEY_CERT_" + Guid.NewGuid().ToString("N");
            string certificatePath = CreateTestCertificate(certificatePassword);
            string? originalValue = Environment.GetEnvironmentVariable(environmentVariable);
            Environment.SetEnvironmentVariable(environmentVariable, certificatePassword);
            try
            {
                var shortKeyOptions = new Claude4NetApiServerOptions
                {
                    BindAddress = "192.0.2.10",
                    AllowRemote = true,
                    ApiKey = new string('s', 31),
                    CertificatePath = certificatePath,
                    CertificatePasswordEnvironmentVariable = environmentVariable
                };

                TargetInvocationException error = Assert.Throws<TargetInvocationException>(
                    () => ValidateOptions(shortKeyOptions));
                ArgumentException keyError = Assert.IsType<ArgumentException>(error.InnerException);
                Assert.Contains("32", keyError.Message, StringComparison.Ordinal);

                var strongKeyOptions = new Claude4NetApiServerOptions
                {
                    BindAddress = "192.0.2.10",
                    AllowRemote = true,
                    ApiKey = new string('s', 32),
                    CertificatePath = certificatePath,
                    CertificatePasswordEnvironmentVariable = environmentVariable
                };
                Assert.NotNull(ValidateOptions(strongKeyOptions));
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, originalValue);
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public void ServerOptions_LoopbackExplicitApiKey_AllowsShortKey()
        {
            var options = new Claude4NetApiServerOptions
            {
                BindAddress = "127.0.0.1",
                ApiKey = "short-key"
            };

            Assert.NotNull(ValidateOptions(options));
        }

        [Fact]
        public async Task ApiCommand_AgentRunEnableSwitch_IsRejectedAsUnknown()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
                () => SystemCommands.HandleApi("start --enable-agent-run", services));

            Assert.Contains("Unknown API option", error.Message, StringComparison.Ordinal);
            Assert.Contains("--enable-agent-run", error.Message, StringComparison.Ordinal);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task ApiStartupOutput_DoesNotAdvertiseAgentRun()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            try
            {
                string output = await SystemCommands.HandleApi(
                    $"start --port {port} --key {TestApiKey}",
                    services);

                Assert.DoesNotContain("/api/v1/agent/run", output, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task AnonymousHealth_ReturnsExactlyHealthyWithoutOperationalFields()
        {
            await using var harness = await ServerHarness.StartAsync();
            using var anonymousClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            using HttpResponseMessage response = await anonymousClient.GetAsync(harness.Url("/api/v1/health"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonProperty property = Assert.Single(document.RootElement.EnumerateObject());
            Assert.Equal("status", property.Name);
            Assert.Equal("healthy", property.Value.GetString());
        }

        [Fact]
        public async Task Cors_AllowsLocalhostOriginsAndRejectsRemoteOrigins()
        {
            await using var harness = await ServerHarness.StartAsync();

            using HttpResponseMessage localResponse = await SendPreflightAsync(harness, "http://localhost:3000");
            Assert.Equal(HttpStatusCode.NoContent, localResponse.StatusCode);
            Assert.Equal("http://localhost:3000", GetCorsOrigin(localResponse));

            using HttpResponseMessage remoteResponse = await SendPreflightAsync(harness, "https://example.test");
            Assert.Null(GetCorsOrigin(remoteResponse));
        }

        [Theory]
        [InlineData("/api/v1/agent/run")]
        [InlineData("/api/v1/agent/run/")]
        [InlineData("/API/V1/AGENT/RUN")]
        [InlineData("/API/V1/AGENT/RUN/")]
        public async Task AgentRun_AuthenticatedAliases_AreDisabledBeforeBodyOrDependencyAccess(string path)
        {
            var services = new ThrowOnResolutionServiceProvider();
            await using var server = new Claude4NetApiServer(services, new ProviderRegistry());
            int port = GetAvailablePort();
            await server.StartAsync(port, TestApiKey);
            using var client = CreateAuthenticatedClient();
            using var content = new StringContent("{", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await client.PostAsync(
                $"http://127.0.0.1:{port}{path}",
                content);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("agent_run_disabled", await ReadErrorCodeAsync(response));
            Assert.Equal(0, services.ResolutionCount);
        }

        [Theory]
        [InlineData("/api/v1/agent/run")]
        [InlineData("/api/v1/agent/run/")]
        [InlineData("/API/V1/AGENT/RUN")]
        [InlineData("/API/V1/AGENT/RUN/")]
        public async Task AgentRun_AnonymousAliases_RemainUnauthorized(string path)
        {
            await using var harness = await ServerHarness.StartAsync();
            using var anonymousClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var content = new StringContent("{", Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await anonymousClient.PostAsync(harness.Url(path), content);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("invalid_api_key", await ReadErrorCodeAsync(response));
        }

        [Fact]
        public async Task AgentRun_BlockingBody_ReturnsForbiddenWithoutWaitingForBody()
        {
            await using var harness = await ServerHarness.StartAsync();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, harness.Port);
            await using NetworkStream stream = client.GetStream();
            byte[] headers = Encoding.ASCII.GetBytes(
                "POST /api/v1/agent/run/ HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{harness.Port}\r\n" +
                $"Authorization: Bearer {TestApiKey}\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: 1024\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers);

            var responseBuffer = new byte[4096];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            int bytesRead = await stream.ReadAsync(responseBuffer, timeout.Token);
            string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

            Assert.StartsWith("HTTP/1.1 403", response, StringComparison.Ordinal);
            Assert.Contains("agent_run_disabled", response, StringComparison.Ordinal);
        }

        [Fact]
        public void AgentProviderResolution_InvalidExplicitProvider_DoesNotFallbackToActiveProvider()
        {
            var activeFactory = new AgentProviderTrackingFactory();
            var registry = new ProviderRegistry();
            registry.Register(Wave2TestSupport.Descriptor(
                "active-provider",
                AgentProviderTrackingFactory.TransportKind,
                "active-model"));
            registry.RegisterFactory(activeFactory);
            using ServiceProvider services = new ServiceCollection()
                .AddSingleton(registry)
                .BuildServiceProvider();
            var server = new Claude4NetApiServer(services);
            AppState.ActiveProvider = "active-provider";
            AppState.ActiveModel = "active-model";

            MethodInfo? resolver = typeof(Claude4NetApiServer).GetMethod(
                "ResolveProviderAndModel",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolver);

            var resolved = ((ILLMProvider? Provider, string Model))resolver!.Invoke(
                server,
                new object?[] { "invalid-model", "invalid-provider" })!;

            Assert.Null(resolved.Provider);
            Assert.Equal("invalid-model", resolved.Model);
            Assert.Equal(0, activeFactory.CreateCount);
        }

        [Fact]
        public async Task StartedServer_UsesImmutableValidatedOptionsSnapshot()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();
            var options = new Claude4NetApiServerOptions
            {
                Port = port,
                ApiKey = "Case-Sensitive-Key",
                MaxRequestBodyBytes = 1_048_576
            };
            try
            {
                await server.StartAsync(options);
                options.Port = GetAvailablePort();
                options.ApiKey = "mutated-key";
                options.BindAddress = "192.0.2.10";
                options.MaxRequestBodyBytes = 1;

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "Case-Sensitive-Key");
                using var content = new StringContent("{", Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(
                    $"http://127.0.0.1:{port}/api/v1/agent/run/",
                    content);

                Assert.Equal($"http://127.0.0.1:{port}", server.Url);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal("agent_run_disabled", await ReadErrorCodeAsync(response));
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task LoopbackWithoutCertificate_UsesHttp()
        {
            await using var harness = await ServerHarness.StartAsync();

            Assert.StartsWith("http://", harness.Server.Url, StringComparison.Ordinal);
            using HttpResponseMessage response = await harness.Client.GetAsync(harness.Url("/api/v1/status"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task LoopbackWithCertificate_UsesHttpsAndDoesNotRetainPassword()
        {
            const string certificatePassword = "test-only-password";
            string environmentVariable = "C4N_TEST_CERT_" + Guid.NewGuid().ToString("N");
            string certificatePath = CreateTestCertificate(certificatePassword);
            Environment.SetEnvironmentVariable(environmentVariable, certificatePassword);
            try
            {
                await using ServiceProvider services = BuildServices();
                Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
                int port = GetAvailablePort();
                var options = new Claude4NetApiServerOptions { Port = port, ApiKey = TestApiKey };
                SetRequiredProperty(options, "CertificatePath", certificatePath);
                SetRequiredProperty(options, "CertificatePasswordEnvironmentVariable", environmentVariable);

                await server.StartAsync(options);
                try
                {
                    Assert.Equal($"https://127.0.0.1:{port}", server.Url);
                    Assert.Null(options.GetType().GetProperty("CertificatePassword"));
                    using var handler = new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
                    using HttpResponseMessage response = await client.GetAsync(server.Url + "/api/v1/health");
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                }
                finally
                {
                    await server.StopAsync();
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, null);
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public void RemoteValidatedSnapshot_RequiresHttpsWithoutBindingRemoteAddress()
        {
            const string certificatePassword = "test-only-password";
            string environmentVariable = "C4N_TEST_CERT_" + Guid.NewGuid().ToString("N");
            string certificatePath = CreateTestCertificate(certificatePassword);
            Environment.SetEnvironmentVariable(environmentVariable, certificatePassword);
            try
            {
                var options = new Claude4NetApiServerOptions
                {
                    Port = 8123,
                    BindAddress = "192.0.2.10",
                    AllowRemote = true
                };
                SetRequiredProperty(options, "CertificatePath", certificatePath);
                SetRequiredProperty(options, "CertificatePasswordEnvironmentVariable", environmentVariable);
                MethodInfo? validate = options.GetType().GetMethod("Validate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(validate);

                object? snapshot = validate!.Invoke(options, null);
                Assert.NotNull(snapshot);

                Assert.Equal("https", GetRequiredProperty(snapshot!, "Scheme"));
                Assert.Equal("192.0.2.10", GetRequiredProperty(snapshot!, "BindAddress").ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable(environmentVariable, null);
                File.Delete(certificatePath);
            }
        }

        [Fact]
        public async Task RequestBody_OverOneMiB_ReturnsRequestTooLarge()
        {
            await using var harness = await ServerHarness.StartAsync();
            string oversizedPrompt = new('x', 1_048_576);

            using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                new
                {
                    model = "security-model",
                    messages = new[] { new { role = "user", content = oversizedPrompt } }
                });

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
            Assert.Equal("request_too_large", await ReadErrorCodeAsync(response));
        }

        [Fact]
        public async Task SeventeenthActiveRequest_WithZeroQueue_ReturnsConcurrencyLimit()
        {
            var coordinator = new DelayedProviderCoordinator();
            await using var harness = await ServerHarness.StartAsync(coordinator);
            Task<HttpResponseMessage>[] requests = Enumerable.Range(0, 17)
                .Select(index => harness.Client.PostAsJsonAsync(
                    harness.Url("/v1/chat/completions"),
                    CreateChatRequest($"request-{index}")))
                .ToArray();

            try
            {
                await coordinator.WaitForEntriesAsync(16, TimeSpan.FromSeconds(5));
                Task<Task<HttpResponseMessage>> anyResponse = Task.WhenAny(requests);
                Task winner = await Task.WhenAny(anyResponse, Task.Delay(TimeSpan.FromSeconds(2)));
                Assert.Same(anyResponse, winner);

                using HttpResponseMessage rejected = await await anyResponse;
                Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
                Assert.Equal("concurrency_limit_exceeded", await ReadErrorCodeAsync(rejected));
            }
            finally
            {
                coordinator.Release();
                HttpResponseMessage[] responses = await Task.WhenAll(requests);
                foreach (HttpResponseMessage response in responses)
                {
                    response.Dispose();
                }
            }
        }

        [Fact]
        public async Task ConfiguredShortTimeout_UsesGatewayTimeoutEnvelope()
        {
            var coordinator = new DelayedProviderCoordinator();
            await using var harness = await ServerHarness.StartWithOptionsAsync(
                coordinator,
                TimeSpan.FromMilliseconds(100));

            Task<HttpResponseMessage> request = harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                CreateChatRequest("timeout-request"));

            try
            {
                await coordinator.WaitForEntriesAsync(1, TimeSpan.FromSeconds(5));
                using HttpResponseMessage response = await request.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
                Assert.Equal("request_timeout", await ReadErrorCodeAsync(response));
            }
            finally
            {
                coordinator.Release();
            }
        }

        [Fact]
        public async Task IncompleteRequestBody_TimesOutWith504AndReclaimsExecutionSlot()
        {
            var coordinator = new DelayedProviderCoordinator();
            coordinator.Release();
            await using var harness = await ServerHarness.StartWithOptionsAsync(
                coordinator,
                TimeSpan.FromMilliseconds(100),
                maxConcurrentRequests: 1);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, harness.Port);
            await using NetworkStream stream = client.GetStream();
            byte[] request = Encoding.ASCII.GetBytes(
                "POST /v1/chat/completions HTTP/1.1\r\n" +
                $"Host: 127.0.0.1:{harness.Port}\r\n" +
                $"Authorization: Bearer {TestApiKey}\r\n" +
                "Content-Type: application/json\r\n" +
                "Content-Length: 1024\r\n" +
                "Connection: close\r\n\r\n" +
                "{\"model\":\"security-model\"");
            await stream.WriteAsync(request);

            string statusLine = await ReadStatusLineAsync(stream).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.StartsWith("HTTP/1.1 504", statusLine, StringComparison.Ordinal);

            using HttpResponseMessage reclaimed = await harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                CreateChatRequest("after-incomplete-body")).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(HttpStatusCode.OK, reclaimed.StatusCode);
        }

        [Fact]
        public async Task ProviderIgnoringCancellation_TimesOutAndReclaimsExecutionSlot()
        {
            var coordinator = new IgnoringCancellationProviderCoordinator();
            await using var harness = await ServerHarness.StartWithOptionsAsync(
                new IgnoringCancellationProviderFactory(coordinator),
                TimeSpan.FromMilliseconds(100),
                maxConcurrentRequests: 1);
            Task<HttpResponseMessage> firstRequest = harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                CreateChatRequest("blocked-provider"));

            try
            {
                await coordinator.WaitForFirstEntryAsync(TimeSpan.FromSeconds(2));
                using HttpResponseMessage timedOut = await firstRequest.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);

                using HttpResponseMessage reclaimed = await harness.Client.PostAsJsonAsync(
                    harness.Url("/v1/chat/completions"),
                    CreateChatRequest("after-provider-timeout")).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(HttpStatusCode.OK, reclaimed.StatusCode);
            }
            finally
            {
                coordinator.Release();
                if (!firstRequest.IsCompleted)
                {
                    try
                    {
                        using HttpResponseMessage cleanup = await firstRequest.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch
                    {
                    }
                }
            }
        }

        [Fact]
        public async Task EnumeratorDisposalIgnoringCancellation_TimesOutAndReclaimsExecutionSlot()
        {
            var coordinator = new EnumeratorDisposalCoordinator(hangFirstRequest: true);
            await using var harness = await ServerHarness.StartWithOptionsAsync(
                new EnumeratorDisposalProviderFactory(coordinator),
                TimeSpan.FromMilliseconds(100),
                maxConcurrentRequests: 1);

            using HttpResponseMessage timedOut = await harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                CreateChatRequest("hanging-disposal")).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
            await coordinator.WaitForDisposalAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(1, coordinator.DisposalCount);

            using HttpResponseMessage reclaimed = await harness.Client.PostAsJsonAsync(
                harness.Url("/v1/chat/completions"),
                CreateChatRequest("after-hanging-disposal")).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(HttpStatusCode.OK, reclaimed.StatusCode);
        }

        [Theory]
        [InlineData("/v1/chat/completions")]
        [InlineData("/v1/completions")]
        public async Task ProviderEnumerator_IsDisposedExactlyOnce_AfterNormalCompletion(string endpoint)
        {
            var coordinator = new EnumeratorDisposalCoordinator(hangFirstRequest: false);
            await using var harness = await ServerHarness.StartWithOptionsAsync(
                new EnumeratorDisposalProviderFactory(coordinator),
                TimeSpan.FromSeconds(2));
            object request = endpoint == "/v1/chat/completions"
                ? CreateChatRequest("normal-disposal")
                : new { model = "security-model", prompt = "normal-disposal", stream = false };

            using HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
                harness.Url(endpoint),
                request).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, coordinator.DisposalCount);
        }

        [Fact]
        public async Task ApiKey_IsShownOnceThenRedactedFromStatusAndAlreadyRunningOutput()
        {
            int port = GetAvailablePort();
            await using ServiceProvider services = BuildServices();

            string started = await SystemCommands.HandleApi($"start {port} {TestApiKey}", services);
            string status = await SystemCommands.HandleApi("status", services);
            string alreadyRunning = await SystemCommands.HandleApi("start", services);

            try
            {
                Assert.Contains(TestApiKey, started, StringComparison.Ordinal);
                Assert.DoesNotContain(TestApiKey, status, StringComparison.Ordinal);
                Assert.DoesNotContain(TestApiKey, alreadyRunning, StringComparison.Ordinal);
                Assert.Equal(1, CountOccurrences(started + status + alreadyRunning, TestApiKey));
            }
            finally
            {
                await services.GetRequiredService<Claude4NetApiServer>().StopAsync();
            }
        }

        [Fact]
        public async Task GeneratedApiKey_RotatesAcrossStopAndRestart()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            await server.StartAsync(GetAvailablePort());
            string? firstApiKey = server.TakeApiKeyForDisplay();
            await server.StopAsync();
            await server.StartAsync(GetAvailablePort());
            string? secondApiKey = server.TakeApiKeyForDisplay();

            Assert.False(string.IsNullOrWhiteSpace(firstApiKey));
            Assert.False(string.IsNullOrWhiteSpace(secondApiKey));
            Assert.NotEqual(firstApiKey, secondApiKey);
        }

        [Fact]
        public async Task GeneratedApiKey_FromPreviousLifecycle_Returns401AfterRestart()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            await server.StartAsync(GetAvailablePort());
            string oldApiKey = Assert.IsType<string>(server.TakeApiKeyForDisplay());
            await server.StopAsync();
            int restartedPort = GetAvailablePort();
            await server.StartAsync(restartedPort);
            string newApiKey = Assert.IsType<string>(server.TakeApiKeyForDisplay());

            using var oldKeyClient = CreateAuthenticatedClient(oldApiKey);
            using HttpResponseMessage rejected = await oldKeyClient.GetAsync(
                $"http://127.0.0.1:{restartedPort}/v1/models");
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.Equal("invalid_api_key", await ReadErrorCodeAsync(rejected));

            using var newKeyClient = CreateAuthenticatedClient(newApiKey);
            using HttpResponseMessage accepted = await newKeyClient.GetAsync(
                $"http://127.0.0.1:{restartedPort}/v1/models");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }

        [Fact]
        public async Task ExplicitApiKey_RemainsValidWhenExplicitlySuppliedToNewLifecycle()
        {
            string apiKey = "c4n-explicit-lifecycle-" + Guid.NewGuid().ToString("N");
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            await server.StartAsync(GetAvailablePort(), apiKey);
            await server.StopAsync();
            int restartedPort = GetAvailablePort();
            await server.StartAsync(restartedPort, apiKey);

            using var client = CreateAuthenticatedClient(apiKey);
            using HttpResponseMessage response = await client.GetAsync(
                $"http://127.0.0.1:{restartedPort}/v1/models");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task FailedStartup_DoesNotRemainRunning_AndRepeatedStopIsSafe()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            Exception? startupError = await Record.ExceptionAsync(() => server.StartAsync(65_536, TestApiKey));
            bool runningAfterFailure = server.IsRunning;
            Exception? firstStopError = await Record.ExceptionAsync(() => server.StopAsync());
            Exception? secondStopError = await Record.ExceptionAsync(() => server.StopAsync());

            Assert.NotNull(startupError);
            Assert.False(runningAfterFailure);
            Assert.Null(firstStopError);
            Assert.Null(secondStopError);
            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task RepeatedStopAndDispose_AreSafe()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            Assert.IsAssignableFrom<IAsyncDisposable>(server);
            var disposable = (IAsyncDisposable)server;

            await server.StartAsync(GetAvailablePort(), TestApiKey);
            await server.StopAsync();
            await server.StopAsync();
            await disposable.DisposeAsync();
            await disposable.DisposeAsync();

            Assert.False(server.IsRunning);
        }

        [Fact]
        public async Task ConcurrentStarts_OnSameInstance_AreSerialized()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
            int port = GetAvailablePort();

            try
            {
                Task firstStart = server.StartAsync(port, TestApiKey);
                Task secondStart = server.StartAsync(port, TestApiKey);

                Exception? error = await Record.ExceptionAsync(() => Task.WhenAll(firstStart, secondStart));

                Assert.Null(error);
                Assert.True(server.IsRunning);
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using HttpResponseMessage health = await client.GetAsync($"http://127.0.0.1:{port}/api/v1/health");
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                AssertLifecycleGate(server);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task StopRacingWithStart_WaitsForStartupThenStopsServer()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            try
            {
                Task start = server.StartAsync(GetAvailablePort(), TestApiKey);
                Task stop = server.StopAsync();

                await Task.WhenAll(start, stop);

                Assert.False(server.IsRunning);
                AssertLifecycleGate(server);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        [Fact]
        public async Task ConcurrentApiKeyDisplay_ExactlyOneCallerReceivesKey()
        {
            await using ServiceProvider services = BuildServices();
            Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();

            try
            {
                await server.StartAsync(GetAvailablePort(), TestApiKey);
                using var release = new ManualResetEventSlim(false);
                Task<string?>[] consumers = Enumerable.Range(0, 64)
                    .Select(_ => Task.Run(() =>
                    {
                        release.Wait();
                        return server.TakeApiKeyForDisplay();
                    }))
                    .ToArray();

                release.Set();
                string?[] results = await Task.WhenAll(consumers);

                Assert.Single(results, value => value == TestApiKey);
                Assert.All(results.Where(value => value != TestApiKey), Assert.Null);
                FieldInfo? availability = typeof(Claude4NetApiServer).GetField(
                    "_isApiKeyAvailableForDisplay",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(availability);
                Assert.Equal(typeof(int), availability!.FieldType);
            }
            finally
            {
                await server.StopAsync();
            }
        }

        private static object CreateChatRequest(string prompt)
        {
            return new
            {
                model = "security-model",
                messages = new[] { new { role = "user", content = prompt } },
                stream = false
            };
        }

        private static void AssertLifecycleGate(Claude4NetApiServer server)
        {
            FieldInfo? lifecycleGate = server.GetType().GetField(
                "_lifecycleGate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(lifecycleGate);
            Assert.Equal(typeof(SemaphoreSlim), lifecycleGate!.FieldType);
        }

        private static async Task<HttpResponseMessage> SendPreflightAsync(ServerHarness harness, string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, harness.Url("/v1/chat/completions"));
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            return await harness.Client.SendAsync(request);
        }

        private static string? GetCorsOrigin(HttpResponseMessage response)
        {
            return response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? values)
                ? Assert.Single(values)
                : null;
        }

        private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
        {
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("error", out JsonElement error) ||
                error.ValueKind != JsonValueKind.Object ||
                !error.TryGetProperty("code", out JsonElement code))
            {
                return null;
            }

            return code.GetString();
        }

        private static async Task<string> ReadStatusLineAsync(NetworkStream stream)
        {
            var response = new StringBuilder();
            var buffer = new byte[1];
            while (!response.ToString().EndsWith("\r\n", StringComparison.Ordinal))
            {
                int read = await stream.ReadAsync(buffer);
                if (read == 0) break;
                response.Append((char)buffer[0]);
            }

            return response.ToString();
        }

        private static Type GetRequiredOptionsType()
        {
            Type? optionsType = typeof(Claude4NetApiServer).Assembly.GetType(
                "Claude4Net.Runtime.ApiServer.Claude4NetApiServerOptions");
            Assert.NotNull(optionsType);
            return optionsType!;
        }

        private static object ValidateOptions(Claude4NetApiServerOptions options)
        {
            MethodInfo? validate = options.GetType().GetMethod(
                "Validate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(validate);
            object? validated = validate!.Invoke(options, null);
            Assert.NotNull(validated);
            return validated!;
        }

        private static object GetRequiredProperty(object instance, string propertyName)
        {
            PropertyInfo? property = instance.GetType().GetProperty(propertyName);
            Assert.NotNull(property);
            object? value = property!.GetValue(instance);
            Assert.NotNull(value);
            return value!;
        }

        private static void SetRequiredProperty(object instance, string propertyName, object value)
        {
            PropertyInfo? property = instance.GetType().GetProperty(propertyName);
            Assert.NotNull(property);
            property!.SetValue(instance, value);
        }

        private static int CountOccurrences(string value, string expected)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(expected, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += expected.Length;
            }

            return count;
        }

        private static ServiceProvider BuildServices(DelayedProviderCoordinator? coordinator = null)
            => coordinator == null
                ? new ServiceCollection().AddSingleton<Claude4NetApiServer>().BuildServiceProvider()
                : BuildServices(new DelayedProviderFactory(coordinator));

        private static ServiceProvider BuildServices(IProviderFactory providerFactory)
        {
            var services = new ServiceCollection();
            var registry = new ProviderRegistry();
            registry.Register(new ProviderDescriptor
            {
                Id = ProviderId,
                Label = "Security delayed provider",
                TransportKind = ProviderId,
                DefaultModels = new ProviderDefaultModels
                {
                    Small = "security-model",
                    Large = "security-model"
                }
            });
            registry.RegisterFactory(providerFactory);
            services.AddSingleton(registry);

            AppState.ActiveProvider = ProviderId;
            AppState.ActiveModel = "security-model";
            AppState.IsProviderExplicitlySet = true;

            services.AddSingleton<Claude4NetApiServer>();
            return services.BuildServiceProvider();
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static HttpClient CreateAuthenticatedClient()
            => CreateAuthenticatedClient(TestApiKey);

        private static HttpClient CreateAuthenticatedClient(string apiKey)
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return client;
        }

        private static string CreateTestCertificate(string password)
        {
            using RSA key = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=localhost",
                key,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));
            var subjectAlternativeName = new SubjectAlternativeNameBuilder();
            subjectAlternativeName.AddDnsName("localhost");
            subjectAlternativeName.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(subjectAlternativeName.Build());
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            using X509Certificate2 certificate = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddDays(1));
            string path = Path.Combine(Path.GetTempPath(), $"c4n-{Guid.NewGuid():N}.pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
            return path;
        }

        private sealed class ThrowOnResolutionServiceProvider : IServiceProvider
        {
            public int ResolutionCount { get; private set; }

            public object? GetService(Type serviceType)
            {
                ResolutionCount++;
                throw new InvalidOperationException($"Unexpected service resolution: {serviceType.FullName}");
            }
        }

        private sealed class AgentProviderTrackingFactory : IProviderFactory
        {
            private int _createCount;

            public const string TransportKind = "agent-provider-tracking";
            public bool SupportsApiRequests => true;
            public int CreateCount => Volatile.Read(ref _createCount);
            public bool CanCreate(ProviderDescriptor descriptor) => descriptor.TransportKind == TransportKind;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
            {
                Interlocked.Increment(ref _createCount);
                return new Wave2EchoProvider();
            }

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => Create(descriptor, serviceProvider);
        }

        private sealed class ServerHarness : IAsyncDisposable
        {
            private readonly ServiceProvider _services;

            private ServerHarness(ServiceProvider services, Claude4NetApiServer server, int port)
            {
                _services = services;
                Server = server;
                Port = port;
                Client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestApiKey);
            }

            public Claude4NetApiServer Server { get; }
            public HttpClient Client { get; }
            public int Port { get; }

            public string Url(string path) => $"http://127.0.0.1:{Port}{path}";

            public static async Task<ServerHarness> StartAsync(DelayedProviderCoordinator? coordinator = null)
            {
                ServiceProvider services = BuildServices(coordinator);
                Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
                int port = GetAvailablePort();
                try
                {
                    await server.StartAsync(port, TestApiKey);
                    return new ServerHarness(services, server, port);
                }
                catch
                {
                    await services.DisposeAsync();
                    throw;
                }
            }

            public static async Task<ServerHarness> StartWithOptionsAsync(
                DelayedProviderCoordinator coordinator,
                TimeSpan requestTimeout,
                int maxConcurrentRequests = 16)
                => await StartWithOptionsCoreAsync(
                    BuildServices(coordinator),
                    requestTimeout,
                    maxConcurrentRequests);

            public static async Task<ServerHarness> StartWithOptionsAsync(
                IProviderFactory providerFactory,
                TimeSpan requestTimeout,
                int maxConcurrentRequests = 16)
                => await StartWithOptionsCoreAsync(
                    BuildServices(providerFactory),
                    requestTimeout,
                    maxConcurrentRequests);

            private static async Task<ServerHarness> StartWithOptionsCoreAsync(
                ServiceProvider services,
                TimeSpan requestTimeout,
                int maxConcurrentRequests)
            {
                Claude4NetApiServer server = services.GetRequiredService<Claude4NetApiServer>();
                int port = GetAvailablePort();
                Type optionsType = GetRequiredOptionsType();
                object options = Activator.CreateInstance(optionsType)!;
                SetRequiredProperty(options, "Port", port);
                SetRequiredProperty(options, "ApiKey", TestApiKey);
                SetRequiredProperty(options, "BindAddress", "127.0.0.1");
                SetRequiredProperty(options, "RequestTimeout", requestTimeout);
                SetRequiredProperty(options, "MaxConcurrentRequests", maxConcurrentRequests);

                MethodInfo? start = typeof(Claude4NetApiServer).GetMethods()
                    .SingleOrDefault(method =>
                        method.Name == nameof(Claude4NetApiServer.StartAsync) &&
                        method.GetParameters().Length == 2 &&
                        method.GetParameters()[0].ParameterType == optionsType);
                Assert.NotNull(start);

                try
                {
                    var task = Assert.IsAssignableFrom<Task>(start!.Invoke(
                        server,
                        new object[] { options, CancellationToken.None }));
                    await task;
                    return new ServerHarness(services, server, port);
                }
                catch
                {
                    await services.DisposeAsync();
                    throw;
                }
            }

            public async ValueTask DisposeAsync()
            {
                Client.Dispose();
                await Server.StopAsync();
                await _services.DisposeAsync();
            }
        }

        private sealed class DelayedProviderCoordinator
        {
            private readonly SemaphoreSlim _entries = new(0);
            private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Enter() => _entries.Release();

            public Task WaitForReleaseAsync(CancellationToken cancellationToken)
                => _release.Task.WaitAsync(cancellationToken);

            public void Release() => _release.TrySetResult();

            public async Task WaitForEntriesAsync(int count, TimeSpan timeout)
            {
                using var cancellation = new CancellationTokenSource(timeout);
                for (int index = 0; index < count; index++)
                {
                    await _entries.WaitAsync(cancellation.Token);
                }
            }
        }

        private sealed class DelayedProviderFactory : IProviderFactory
        {
            private readonly DelayedProviderCoordinator _coordinator;

            public DelayedProviderFactory(DelayedProviderCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public bool SupportsApiRequests => true;

            public bool CanCreate(ProviderDescriptor descriptor) => descriptor.Id == ProviderId;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new DelayedProvider(_coordinator);

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new DelayedProvider(_coordinator);
        }

        private sealed class DelayedProvider : ILLMProvider
        {
            private readonly DelayedProviderCoordinator _coordinator;
            private readonly List<object> _history = new();

            public DelayedProvider(DelayedProviderCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public string Name => "Security delayed provider";
            public ITokenCounter TokenCounter { get; } = new TestTokenCounter();
            public int ContextLimit => 4096;

            public async IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                _coordinator.Enter();
                await _coordinator.WaitForReleaseAsync(ct);
                yield return new LLMStreamEvent
                {
                    Type = LLMStreamEventType.TextDelta,
                    Delta = "released"
                };
            }

            public void AddMessage(object message) => _history.Add(message);

            public IReadOnlyList<object> GetHistory() => _history.ToArray();

            public void SetHistory(IEnumerable<object> history)
            {
                _history.Clear();
                _history.AddRange(history);
            }
        }

        private sealed class IgnoringCancellationProviderCoordinator
        {
            private readonly SemaphoreSlim _firstEntry = new(0, 1);
            private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _advanceCount;

            public ValueTask<bool> AdvanceAsync()
            {
                if (Interlocked.Increment(ref _advanceCount) != 1) return ValueTask.FromResult(true);
                _firstEntry.Release();
                return new ValueTask<bool>(_release.Task);
            }

            public void Release() => _release.TrySetResult(true);

            public async Task WaitForFirstEntryAsync(TimeSpan timeout)
            {
                using var cancellation = new CancellationTokenSource(timeout);
                await _firstEntry.WaitAsync(cancellation.Token);
            }
        }

        private sealed class IgnoringCancellationProviderFactory : IProviderFactory
        {
            private readonly IgnoringCancellationProviderCoordinator _coordinator;

            public IgnoringCancellationProviderFactory(IgnoringCancellationProviderCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public bool SupportsApiRequests => true;

            public bool CanCreate(ProviderDescriptor descriptor) => descriptor.Id == ProviderId;

            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new IgnoringCancellationProvider(_coordinator);

            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new IgnoringCancellationProvider(_coordinator);
        }

        private sealed class IgnoringCancellationProvider : ILLMProvider
        {
            private readonly IgnoringCancellationProviderCoordinator _coordinator;

            public IgnoringCancellationProvider(IgnoringCancellationProviderCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public string Name => "Security cancellation-ignoring provider";
            public ITokenCounter TokenCounter { get; } = new TestTokenCounter();
            public int ContextLimit => 4096;

            public IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                CancellationToken ct = default)
                => new IgnoringCancellationEnumerable(_coordinator);

            public void AddMessage(object message)
            {
            }

            public IReadOnlyList<object> GetHistory() => Array.Empty<object>();

            public void SetHistory(IEnumerable<object> history)
            {
            }
        }

        private sealed class IgnoringCancellationEnumerable : IAsyncEnumerable<LLMStreamEvent>, IAsyncEnumerator<LLMStreamEvent>
        {
            private readonly IgnoringCancellationProviderCoordinator _coordinator;
            private bool _hasAdvanced;

            public IgnoringCancellationEnumerable(IgnoringCancellationProviderCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public LLMStreamEvent Current { get; } = new()
            {
                Type = LLMStreamEventType.TextDelta,
                Delta = "released"
            };

            public IAsyncEnumerator<LLMStreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_hasAdvanced) return ValueTask.FromResult(false);
                _hasAdvanced = true;
                return _coordinator.AdvanceAsync();
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class EnumeratorDisposalCoordinator
        {
            private readonly bool _hangFirstRequest;
            private readonly TaskCompletionSource _disposalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _disposalCount;
            private int _enumeratorCount;

            public EnumeratorDisposalCoordinator(bool hangFirstRequest)
            {
                _hangFirstRequest = hangFirstRequest;
            }

            public int DisposalCount => Volatile.Read(ref _disposalCount);

            public EnumeratorDisposalEnumerable CreateEnumerable()
            {
                bool shouldHang = _hangFirstRequest && Interlocked.Increment(ref _enumeratorCount) == 1;
                return new EnumeratorDisposalEnumerable(this, shouldHang);
            }

            public void RecordDisposal()
            {
                Interlocked.Increment(ref _disposalCount);
                _disposalStarted.TrySetResult();
            }

            public Task WaitForDisposalAsync(TimeSpan timeout) => _disposalStarted.Task.WaitAsync(timeout);
        }

        private sealed class EnumeratorDisposalProviderFactory : IProviderFactory
        {
            private readonly EnumeratorDisposalCoordinator _coordinator;

            public EnumeratorDisposalProviderFactory(EnumeratorDisposalCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public bool SupportsApiRequests => true;
            public bool CanCreate(ProviderDescriptor descriptor) => descriptor.Id == ProviderId;
            public ILLMProvider Create(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new EnumeratorDisposalProvider(_coordinator);
            public ILLMProvider CreateRequestProvider(ProviderDescriptor descriptor, IServiceProvider serviceProvider)
                => new EnumeratorDisposalProvider(_coordinator);
        }

        private sealed class EnumeratorDisposalProvider : ILLMProvider
        {
            private readonly EnumeratorDisposalCoordinator _coordinator;

            public EnumeratorDisposalProvider(EnumeratorDisposalCoordinator coordinator)
            {
                _coordinator = coordinator;
            }

            public string Name => "Security disposal-tracking provider";
            public ITokenCounter TokenCounter { get; } = new TestTokenCounter();
            public int ContextLimit => 4096;
            public IAsyncEnumerable<LLMStreamEvent> StreamQueryAsync(
                string prompt,
                string? model = null,
                CancellationToken ct = default)
                => _coordinator.CreateEnumerable();
            public void AddMessage(object message) { }
            public IReadOnlyList<object> GetHistory() => Array.Empty<object>();
            public void SetHistory(IEnumerable<object> history) { }
        }

        private sealed class EnumeratorDisposalEnumerable : IAsyncEnumerable<LLMStreamEvent>, IAsyncEnumerator<LLMStreamEvent>
        {
            private readonly EnumeratorDisposalCoordinator _coordinator;
            private readonly bool _shouldHang;
            private readonly TaskCompletionSource<bool> _neverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _advanceCount;

            public EnumeratorDisposalEnumerable(EnumeratorDisposalCoordinator coordinator, bool shouldHang)
            {
                _coordinator = coordinator;
                _shouldHang = shouldHang;
            }

            public LLMStreamEvent Current { get; } = new()
            {
                Type = LLMStreamEventType.TextDelta,
                Delta = "completed"
            };

            public IAsyncEnumerator<LLMStreamEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_shouldHang) return new ValueTask<bool>(_neverCompletes.Task);
                return ValueTask.FromResult(Interlocked.Increment(ref _advanceCount) == 1);
            }

            public ValueTask DisposeAsync()
            {
                _coordinator.RecordDisposal();
                return _shouldHang ? new ValueTask(_neverCompletes.Task) : ValueTask.CompletedTask;
            }
        }

        private sealed class TestTokenCounter : ITokenCounter
        {
            public int CountTokens(string text) => text.Length;
            public int CountTokens(object message) => 1;
            public int CountTokens(IEnumerable<object> messages) => messages.Count();
        }
    }
}
