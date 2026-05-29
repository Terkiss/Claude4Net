using System;
using Xunit;
using Claude4Net.Cli.Bootstrap;
using Claude4Net.Runtime;
using Claude4Net.SDK;

namespace Claude4Net.Tests;

[Trait("Category", "K098")]
public class K098Tests
{
    [Fact]
    public void CliOptions_Parse_ApiArguments()
    {
        // Test combinations of --api, --api-host, --api-port
        var args1 = new[] { "--api", "true", "--api-host", "127.0.0.1", "--api-port", "8080" };
        var opt1 = CliOptions.Parse(args1);
        Assert.True(opt1.EnableApi);
        Assert.Equal("127.0.0.1", opt1.ApiHost);
        Assert.Equal(8080, opt1.ApiPort);

        var args2 = new[] { "--api", "false" };
        var opt2 = CliOptions.Parse(args2);
        Assert.False(opt2.EnableApi);

        var args3 = new[] { "--api", "--api-host", "0.0.0.0" };
        var opt3 = CliOptions.Parse(args3);
        Assert.True(opt3.EnableApi);
        Assert.Equal("0.0.0.0", opt3.ApiHost);
        Assert.Null(opt3.ApiPort);
    }

    [Fact]
    public void AppStateSnapshot_CaptureAndRestore()
    {
        // Store current values to restore at the end of the test
        var originalCwd = AppState.CurrentCwd;
        var originalSessionId = AppState.SessionId;
        var originalProvider = AppState.ActiveProvider;
        var originalModel = AppState.ActiveModel;
        var originalPermission = AppState.CurrentPermissionMode;

        try
        {
            // Set initial state
            AppState.CurrentCwd = @"C:\InitialPath";
            AppState.SessionId = "session-initial";
            AppState.ActiveProvider = "initial-provider";
            AppState.ActiveModel = "initial-model";
            AppState.CurrentPermissionMode = PermissionMode.ReadOnly;

            // Capture snapshot
            var snapshot = AppStateSnapshot.Capture();

            // Mutate AppState
            AppState.CurrentCwd = @"D:\MutatedPath";
            AppState.SessionId = "session-mutated";
            AppState.ActiveProvider = "mutated-provider";
            AppState.ActiveModel = "mutated-model";
            AppState.CurrentPermissionMode = PermissionMode.DangerFullAccess;

            // Assert mutations occurred
            Assert.Equal(@"D:\MutatedPath", AppState.CurrentCwd);
            Assert.Equal("session-mutated", AppState.SessionId);
            Assert.Equal("mutated-provider", AppState.ActiveProvider);
            Assert.Equal("mutated-model", AppState.ActiveModel);
            Assert.Equal(PermissionMode.DangerFullAccess, AppState.CurrentPermissionMode);

            // Restore snapshot
            snapshot.Restore();

            // Assert restored values match initial state
            Assert.Equal(@"C:\InitialPath", AppState.CurrentCwd);
            Assert.Equal("session-initial", AppState.SessionId);
            Assert.Equal("initial-provider", AppState.ActiveProvider);
            Assert.Equal("initial-model", AppState.ActiveModel);
            Assert.Equal(PermissionMode.ReadOnly, AppState.CurrentPermissionMode);
        }
        finally
        {
            // Clean up back to original values
            AppState.CurrentCwd = originalCwd;
            AppState.SessionId = originalSessionId;
            AppState.ActiveProvider = originalProvider;
            AppState.ActiveModel = originalModel;
            AppState.CurrentPermissionMode = originalPermission;
        }
    }
}
