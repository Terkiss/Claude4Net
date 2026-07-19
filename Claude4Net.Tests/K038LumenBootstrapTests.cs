using Xunit;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.SDK;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.Runtime;

namespace Claude4Net.Tests;

public class K038LumenBootstrapTests
{
    [Fact]
    public void CliOptions_Parse_BasicOptions()
    {
        var args = new[] { "--smoke-exit", "--provider", "gemini", "--model", "test-model" };

        // Act
        var options = CliOptions.Parse(args);

        // Assert
        Assert.True(options.SmokeExit);
        Assert.Equal("gemini", options.Provider);
        Assert.Equal("test-model", options.Model);
        Assert.Null(options.ValidationError);
    }

    [Fact]
    public void CliOptions_Parse_PermissionMode()
    {
        // Arrange
        var args = new[] { "--permission-mode", "yolo" };

        // Act
        var options = CliOptions.Parse(args);

        // Assert
        Assert.Equal("yolo", options.PermissionModeArg);

        bool success = CliOptions.TryParsePermissionMode(options.PermissionModeArg ?? "", out var mode);
        Assert.True(success);
        Assert.Equal(PermissionMode.Yolo, mode);
    }

    [Fact]
    public void CliOptions_Parse_DoctorCommand()
    {
        // Arrange
        var args = new[] { "doctor", "--output-format", "json" };

        // Act
        var options = CliOptions.Parse(args);

        // Assert
        Assert.True(options.IsDoctor);
        Assert.Equal("--output-format json", options.DoctorArgs);
    }

    [Fact]
    public void CliServiceRegistration_RegistersCoreServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        CliServiceRegistration.ConfigureServices(services);
        var sp = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(sp.GetService<ProviderRegistry>());
        Assert.NotNull(sp.GetService<ISmartRouter>());
        Assert.NotNull(sp.GetService<ToolOrchestrator>());
        Assert.NotNull(sp.GetService<SkillRegistryService>());
    }
}
