using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.AspNetCore.SignalR;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Dashboard.Services;
using Claude4Net.Dashboard;
using System.Net.Http;
using System.Net.Sockets;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace Claude4Net.Tests
{
    public class K028DashboardTests
    {
        [Fact]
        public void DashboardServer_ResolvePort_UsesConfiguredPort()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:Port"] = "5123"
                })
                .Build();

            Assert.Equal(5123, DashboardServer.ResolvePort(configuration, null));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("65536")]
        [InlineData("not-a-port")]
        public void DashboardServer_ResolvePort_RejectsInvalidConfiguredPort(string value)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:Port"] = value
                })
                .Build();

            var error = Assert.Throws<InvalidOperationException>(() => DashboardServer.ResolvePort(configuration, null));
            Assert.Contains("1 to 65535", error.Message);
        }

        [Fact]
        public void DashboardServer_ResolvePort_EnvironmentOverridesConfiguration()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:Port"] = "5123"
                })
                .Build();

            Assert.Equal(6123, DashboardServer.ResolvePort(configuration, "6123"));
        }

        [Fact]
        public async Task SignalR_EventBroadcast_CallsClientsAll()
        {
            // Arrange
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

            var mockHubContext = new Mock<IHubContext<AgentHub>>();
            mockHubContext.Setup(c => c.Clients).Returns(mockClients.Object);

            var broadcaster = new SignalRBroadcaster(mockHubContext.Object);
            var testEvent = new AgentThoughtEvent { Thought = "Test thought" };

            // Act
            await broadcaster.BroadcastAsync(testEvent);

            // Assert
            mockClientProxy.Verify(
                c => c.SendCoreAsync(
                    "ReceiveEvent",
                    It.Is<object[]>(o => o.Contains(testEvent)),
                    default),
                Times.Once);
        }

        [Fact]
        public async Task ApprovalQueue_Broadcast_CallsClientsAll()
        {
            // Arrange
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

            var mockHubContext = new Mock<IHubContext<AgentHub>>();
            mockHubContext.Setup(c => c.Clients).Returns(mockClients.Object);

            var broadcaster = new SignalRBroadcaster(mockHubContext.Object);
            string requestId = "req-123";
            string message = "Test approval";

            // Act
            await broadcaster.BroadcastApprovalRequestAsync(requestId, message);

            // Assert
            mockClientProxy.Verify(
                c => c.SendCoreAsync(
                    "ReceiveApprovalRequest",
                    It.Is<object[]>(o => o[0].ToString() == requestId && o[1].ToString() == message),
                    default),
                Times.Once);
        }

        [Fact]
        public async Task DashboardServer_StartAsync_ThrowsOnPortConflict()
        {
            // Arrange: Occupy port 5001 beforehand
            int testPort = GetFreePort();
            var listener = new TcpListener(IPAddress.Loopback, testPort);
            listener.Start();

            try
            {
                // Act & Assert: Verify exception when trying to start on the same port
                await Assert.ThrowsAnyAsync<Exception>(() => DashboardServer.StartAsync(Array.Empty<string>(), testPort));
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task DashboardServer_StartupAndRoute_ShouldNotHaveAmbiguity()
        {
            // Arrange: Real startup test on port 5002
            int testPort = GetFreePort();
            try
            {
                await DashboardServer.StartAsync(Array.Empty<string>(), testPort);

                using var client = new HttpClient();
                // Act: Call root path
                var response = await client.GetAsync($"http://localhost:{testPort}/");

                // Assert: Verify 200 OK (Avoids 500 AmbiguousMatchException)
                Assert.True(response.IsSuccessStatusCode, $"Dashboard should return success, but got {response.StatusCode}");
            }
            finally
            {
                await DashboardServer.StopAsync();
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
