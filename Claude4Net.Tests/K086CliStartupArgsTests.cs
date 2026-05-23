using Xunit;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using System;

namespace Claude4Net.Tests;

public class K086CliStartupArgsTests
{
    [Fact]
    public void CliOptions_Parse_YoloFlag()
    {
        // Arrange
        var args = new[] { "--yolo" };

        // Act
        var options = CliOptions.Parse(args);

        // Assert
        Assert.Equal("yolo", options.PermissionModeArg);
    }

    [Theory]
    [InlineData(PathSafetyResult.Workspace)]
    [InlineData(PathSafetyResult.SafeSystem)]
    public void PermissionEnforcer_Evaluate_Yolo_InsideWorkspace_ReturnsAllow(PathSafetyResult pathSafety)
    {
        // Arrange
        var enforcer = new PermissionEnforcer();
        var mode = PermissionMode.Yolo; // Normalized to DangerFullAccess
        var commandRisk = new CommandRiskAssessment(CommandRiskLevel.Dangerous, "Dangerous command execution", Array.Empty<string>());

        // Act
        var result = enforcer.Evaluate(mode, "write_file", pathSafety, isSensitiveTool: true, commandRisk);

        // Assert
        Assert.Equal(PermissionDecision.Allow, result.Decision);
        Assert.Contains("DangerFullAccess", result.Reason);
    }

    [Fact]
    public void PermissionEnforcer_Evaluate_Yolo_OutsideWorkspace_ReturnsRequireApproval()
    {
        // Arrange
        var enforcer = new PermissionEnforcer();
        var mode = PermissionMode.Yolo; // Normalized to DangerFullAccess
        var commandRisk = new CommandRiskAssessment(CommandRiskLevel.Dangerous, "Dangerous command execution", Array.Empty<string>());

        // Act
        var result = enforcer.Evaluate(mode, "write_file", PathSafetyResult.Outside, isSensitiveTool: true, commandRisk);

        // Assert
        Assert.Equal(PermissionDecision.RequireApproval, result.Decision);
        Assert.Contains("outside workspace", result.Reason);
    }

    [Fact]
    public void PermissionEnforcer_Evaluate_StandardMode_OutsideWorkspace_ReturnsDeny()
    {
        // Arrange
        var enforcer = new PermissionEnforcer();
        var mode = PermissionMode.WorkspaceWrite;
        var commandRisk = new CommandRiskAssessment(CommandRiskLevel.Dangerous, "Dangerous command execution", Array.Empty<string>());

        // Act
        var result = enforcer.Evaluate(mode, "write_file", PathSafetyResult.Outside, isSensitiveTool: true, commandRisk);

        // Assert
        Assert.Equal(PermissionDecision.Deny, result.Decision);
        Assert.Contains("outside workspace", result.Reason);
    }
}
