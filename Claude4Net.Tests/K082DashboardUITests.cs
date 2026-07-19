using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Claude4Net.Dashboard;
using Claude4Net.Dashboard.Client.Models;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K082DashboardUITests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly string _originalCwd;
        private readonly PermissionMode _originalPermissionMode;
        private readonly string _originalSessionId;
        private const int TestPort = 5082;
        private string[] ServerArgs => new[]
        {
            "--DashboardAuth:TestAuth:Enabled=true",
            $"--DashboardAuth:DataRoot={_tempWorkspace}"
        };

        public K082DashboardUITests()
        {
            _originalCwd = AppState.CurrentCwd ?? string.Empty;
            _originalPermissionMode = AppState.CurrentPermissionMode;
            _originalSessionId = AppState.SessionId;
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K082_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = "test-session-k082";
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.CurrentPermissionMode = _originalPermissionMode;
            AppState.SessionId = _originalSessionId;
            try
            {
                if (Directory.Exists(_tempWorkspace))
                {
                    Directory.Delete(_tempWorkspace, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task AuthMe_WithFakeAuthHeaders_ShouldReturnCurrentDashboardUser()
        {
            await DashboardServer.StartAsync(ServerArgs, TestPort);

            try
            {
                using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{TestPort}") };
                client.DefaultRequestHeaders.Add("X-Test-Sub", "google-sub-k082");
                client.DefaultRequestHeaders.Add("X-Test-Email", "approver@example.com");
                client.DefaultRequestHeaders.Add("X-Test-Role", "Approver");

                var user = await client.GetFromJsonAsync<DashboardUserDto>("/api/auth/me");

                Assert.NotNull(user);
                Assert.True(user.IsAuthenticated);
                Assert.Equal("approver@example.com", user.Email);
                Assert.Equal("Approver", user.Role);
                Assert.True(user.CanApproveSkills);
                Assert.False(user.CanRunRoutines);
            }
            finally
            {
                await DashboardServer.StopAsync();
            }
        }

        [Fact]
        public async Task ControlPlaneHub_WithoutAuth_ShouldRejectConnection()
        {
            await DashboardServer.StartAsync(ServerArgs, TestPort);

            try
            {
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{TestPort}/controlPlaneHub")
                    .Build();

                await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
            }
            finally
            {
                await DashboardServer.StopAsync();
            }
        }

        [Fact]
        public async Task ControlPlaneHub_IsMappedAndAccessible_UnderControlPlaneHubUrl()
        {
            // Start the dashboard server on TestPort
            await DashboardServer.StartAsync(ServerArgs, TestPort);

            try
            {
                // Create a connection to /controlPlaneHub
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{TestPort}/controlPlaneHub", options =>
                    {
                        options.Headers.Add("X-Test-Sub", "google-sub-k082");
                        options.Headers.Add("X-Test-Email", "operator@example.com");
                        options.Headers.Add("X-Test-Role", "Operator");
                    })
                    .Build();

                await connection.StartAsync();

                Assert.Equal(HubConnectionState.Connected, connection.State);
            }
            finally
            {
                await DashboardServer.StopAsync();
            }
        }

        [Fact]
        public async Task ControlPlaneHub_InvokeMethods_ShouldReturnExpectedStructures()
        {
            // Start the dashboard server on TestPort
            await DashboardServer.StartAsync(ServerArgs, TestPort);

            try
            {
                // Create a connection to /controlPlaneHub
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{TestPort}/controlPlaneHub", options =>
                    {
                        options.Headers.Add("X-Test-Sub", "google-sub-k082");
                        options.Headers.Add("X-Test-Email", "operator@example.com");
                        options.Headers.Add("X-Test-Role", "Operator");
                    })
                    .Build();

                await connection.StartAsync();

                // Test GetState
                var state = await connection.InvokeAsync<StateControlPlaneState>("GetState", "test-session-k082");
                Assert.NotNull(state);
                Assert.NotNull(state.MemoryTables);

                // Test GetProviders
                var providersState = await connection.InvokeAsync<ProviderControlPlaneState>("GetProviders");
                Assert.NotNull(providersState);
                Assert.NotNull(providersState.Providers);

                // Test GetRoutines
                var routinesState = await connection.InvokeAsync<RoutineControlPlaneState>("GetRoutines");
                Assert.NotNull(routinesState);
                Assert.NotNull(routinesState.Routines);
            }
            finally
            {
                await DashboardServer.StopAsync();
            }
        }
    }
}
