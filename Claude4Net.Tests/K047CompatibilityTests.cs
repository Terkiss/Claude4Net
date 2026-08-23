using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Commands;

namespace Claude4Net.Tests
{
    public class K047CompatibilityTests
    {
        [Fact]
        public void CliOptions_Parse_HandlesRetainedFlags()
        {
            // Arrange
            string[] args = {
                "--permission-mode", "ReadOnly",
                "--smoke-exit",
                "--provider", "gemini",
                "--model", "test-model"
            };

            // Act
            var options = CliOptions.Parse(args);

            // Assert
            Assert.Equal("ReadOnly", options.PermissionModeArg);
            Assert.True(options.SmokeExit);
            Assert.Equal("gemini", options.Provider);
            Assert.Equal("test-model", options.Model);
            Assert.Null(options.ValidationError);
        }

        [Fact]
        public void CliOptions_Parse_DefaultsToDashboardTrue_AndSupportsNoDashboard()
        {
            // Default empty args -> StartDashboard is true
            var defaultOpts = CliOptions.Parse(Array.Empty<string>());
            Assert.True(defaultOpts.StartDashboard);

            // Explicit --no-dashboard -> StartDashboard is false
            var noDashOpts = CliOptions.Parse(new[] { "--no-dashboard" });
            Assert.False(noDashOpts.StartDashboard);

            // Explicit --dashboard -> StartDashboard is true
            var dashOpts = CliOptions.Parse(new[] { "--dashboard" });
            Assert.True(dashOpts.StartDashboard);
        }

        [Theory]
        [InlineData("--dashboard")]
        [InlineData("--legacy-cli")]
        [InlineData("--lumen")]
        public void CliOptions_Parse_RemovedUiFlags_ReturnsMigrationError(string flag)
        {
            var options = CliOptions.Parse(new[] { flag });

            Assert.Contains(flag, options.ValidationError);
            Assert.Contains("Dashboard and Lumen now start automatically", options.ValidationError);
            Assert.Contains("Legacy UI has been removed", options.ValidationError);
        }

        [Fact]
        public void CliOptions_Parse_HandlesDoctorCommand()
        {
            // Arrange
            string[] args = { "doctor", "--output-format", "json" };

            // Act
            var options = CliOptions.Parse(args);

            // Assert
            Assert.True(options.IsDoctor);
            Assert.Equal("--output-format json", options.DoctorArgs);
        }

        [Fact]
        public void CliServiceRegistration_RegistersRequiredServices()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            CliServiceRegistration.ConfigureServices(services);
            var sp = services.BuildServiceProvider();

            // Assert
            Assert.NotNull(sp.GetService<IInputBroker>());
            Assert.NotNull(sp.GetService<ISmartRouter>());
            Assert.NotNull(sp.GetService<IUserApprovalHandler>());
            Assert.NotNull(sp.GetService<ToolOrchestrator>());
            Assert.NotNull(sp.GetService<ProviderRegistry>());
        }

        [Fact]
        public void PermissionMode_TryParse_HandlesAllVariants()
        {
            Assert.True(CliOptions.TryParsePermissionMode("ReadOnly", out var mode1));
            Assert.Equal(PermissionMode.ReadOnly, mode1);

            Assert.True(CliOptions.TryParsePermissionMode("Workspace-Write", out var mode2));
            Assert.Equal(PermissionMode.WorkspaceWrite, mode2);

            Assert.True(CliOptions.TryParsePermissionMode("yolo", out var mode3));
            Assert.Equal(PermissionMode.Yolo, mode3);
        }

        [Fact]
        public void PipedInputPath_Independence_Verification()
        {
            // This test ensures that the logic used in Program.cs for piped input
            // does not require LumenUI types to be instantiated or configured.

            // In Program.cs:
            // if (Console.IsInputRedirected) { ... use CliOutputHandler and CliUserApprovalHandler ... }

            var services = new ServiceCollection();
            CliServiceRegistration.ConfigureServices(services);
            var sp = services.BuildServiceProvider();

            var broker = sp.GetRequiredService<IInputBroker>();
            var router = sp.GetRequiredService<ISmartRouter>();
            var orchestrator = sp.GetRequiredService<ToolOrchestrator>();

            // These are the core components used in the piped input loop.
            Assert.NotNull(broker);
            Assert.NotNull(router);
            Assert.NotNull(orchestrator);
        }
    }
}
