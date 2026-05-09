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

namespace Claude4Net.Tests
{
    public class K028DashboardTests
    {
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
    }
}
