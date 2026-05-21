using System;
using System.Threading.Tasks;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K065DashboardControlPlaneTests
    {
        [Fact]
        public async Task ExecuteCommand_AnyCommand_ShouldReturnDenyMessage()
        {
            var hub = new ControlPlaneHub();
            var result = await hub.ExecuteCommand("/routine list");
            Assert.Contains("Execution denied", result);
        }
    }
}
