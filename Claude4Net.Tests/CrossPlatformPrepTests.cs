using Xunit;
using Claude4Net.Api;
using Claude4Net.Tools;
using System.Runtime.InteropServices;
using System;

namespace Claude4Net.Tests
{
    public class CrossPlatformPrepTests
    {
        [Fact]
        public void GeminiCliProvider_CommandConstruction_MatchesOS()
        {
            // Windows
            var (winFile, winArgs) = GeminiCliProvider.GetExecutionCommand("gemini-pro", isWindows: true);
            Assert.Equal("cmd.exe", winFile);
            Assert.Contains("/c gemini", winArgs);
            Assert.Contains("-m \"gemini-pro\"", winArgs);

            // Unix (macOS/Linux)
            var (unixFile, unixArgs) = GeminiCliProvider.GetExecutionCommand("gemini-pro", isWindows: false);
            Assert.Equal("/bin/bash", unixFile);
            Assert.Contains("-lc", unixArgs);
            // Verify shell-safe single quoting for inner arguments
            Assert.Contains("-m 'gemini-pro'", unixArgs);
            // Explicitly ensure broken double quote pattern is NOT present
            Assert.DoesNotContain("-m \"gemini-pro\"", unixArgs);

            var (_, quotedArgs) = GeminiCliProvider.GetExecutionCommand("gemini'pro", isWindows: false);
            Assert.Contains("-m 'gemini'\\''pro'", quotedArgs);
        }

        [Fact]
        public void BashTool_ShellConfiguration_MatchesOS()
        {
            // Windows
            var (winFile, winArgs) = BashTool.GetShellConfiguration("echo hello", isWindows: true);
            Assert.Equal("powershell.exe", winFile);
            Assert.Contains("-Command \"echo hello\"", winArgs);

            // Unix
            var (unixFile, unixArgs) = BashTool.GetShellConfiguration("echo hello", isWindows: false);
            // On Windows test environment, /bin/bash might not exist so fallback to /bin/sh is possible in actual logic,
            // but for pure string test we check the logic flow.
            Assert.True(unixFile == "/bin/bash" || unixFile == "/bin/sh");
            Assert.Equal("-c \"echo hello\"", unixArgs);
        }
    }
}
