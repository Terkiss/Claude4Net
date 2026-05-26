using System;
using System.IO;
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
        public async Task ControlPlaneHub_IsMappedAndAccessible_UnderControlPlaneHubUrl()
        {
            // Start the dashboard server on TestPort
            await DashboardServer.StartAsync(Array.Empty<string>(), TestPort);

            try
            {
                // Create a connection to /controlPlaneHub
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{TestPort}/controlPlaneHub")
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
            await DashboardServer.StartAsync(Array.Empty<string>(), TestPort);

            try
            {
                // Create a connection to /controlPlaneHub
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{TestPort}/controlPlaneHub")
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
