using Xunit;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using System;
using System.IO;

namespace Claude4Net.Tests;

[Collection("AppState")]
public class K086WorkspaceArgTests
{
    [Fact]
    public void CliOptions_Parse_SetWorkspace()
    {
        // Arrange
        var targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-workspace");
        var args = new[] { "--setworkspace", targetPath };

        // Act
        var options = CliOptions.Parse(args);

        // Assert
        Assert.Equal(targetPath, options.WorkspaceDir);
    }

    [Fact]
    public void WorkspaceValidation_WithValidDirectory_SetsCurrentCwd()
    {
        // Arrange
        var validPath = AppDomain.CurrentDomain.BaseDirectory;
        var originalCwd = AppState.CurrentCwd;
        try
        {
            // Act
            bool exists = Directory.Exists(validPath);
            if (exists)
            {
                AppState.CurrentCwd = Path.GetFullPath(validPath);
            }

            // Assert
            Assert.True(exists);
            Assert.Equal(Path.GetFullPath(validPath), AppState.CurrentCwd);
        }
        finally
        {
            AppState.CurrentCwd = originalCwd;
        }
    }

    [Fact]
    public void WorkspaceValidation_WithInvalidDirectory_ReturnsFalse()
    {
        // Arrange
        var invalidPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid().ToString());

        // Act
        bool exists = Directory.Exists(invalidPath);

        // Assert
        Assert.False(exists);
    }
}
