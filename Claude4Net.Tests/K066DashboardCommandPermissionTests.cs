using System;
using System.Threading.Tasks;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K066DashboardCommandPermissionTests
    {
        [Fact]
        public async Task ExecuteCommand_SkillApply_ShouldReturnDenyMessage()
        {
            AppState.CurrentCwd = string.Empty;
            var hub = new ControlPlaneHub();
            var result = await hub.ExecuteCommand("/skill apply 123");
            Assert.Contains("Execution denied", result);
        }
    }
}
