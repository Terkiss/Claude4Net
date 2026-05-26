using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K095RedTeamSecurityTests : IDisposable
    {
        private readonly string _testWorkspaceDir;
        private readonly string _originalCwd;
        private readonly SecurityPolicyConfig _originalConfig;

        public K095RedTeamSecurityTests()
        {
            _originalCwd = AppState.CurrentCwd ?? string.Empty;
            _originalConfig = PermissionEnforcer.Config;
            _testWorkspaceDir = Path.Combine(Path.GetTempPath(), "K095_Workspace_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testWorkspaceDir);
            AppState.CurrentCwd = _testWorkspaceDir;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            PermissionEnforcer.Config = _originalConfig;
            PermissionEnforcer.ActiveCommand.Value = null;
            PermissionEnforcer.ActivePath.Value = null;
            try
            {
                if (Directory.Exists(_testWorkspaceDir))
                {
                    Directory.Delete(_testWorkspaceDir, true);
                }
            }
            catch { }
        }

        [Fact]
        public void SecurityPolicyConfig_DefaultValues_AreCorrect()
        {
            var config = SecurityPolicyConfig.CreateDefault();
            Assert.Equal("Strict", config.ActiveProfile);
            Assert.True(config.Profiles.ContainsKey("Strict"));
            Assert.True(config.Profiles.ContainsKey("Permissive"));
            Assert.True(config.Profiles.ContainsKey("Development"));

            var strict = config.Profiles["Strict"];
            Assert.Equal(SecurityProfileLevel.Strict, strict.Level);
            Assert.False(strict.AllowOutsideWorkspace);
            Assert.True(strict.BlockDirectoryTraversal);

            var development = config.Profiles["Development"];
            Assert.Equal(SecurityProfileLevel.Development, development.Level);
            Assert.True(development.AllowOutsideWorkspace);
            Assert.False(development.BlockDirectoryTraversal);
        }

        [Fact]
        public void SecurityPolicyConfig_ParseJson_Works()
        {
            string json = @"
            {
                ""ActiveProfile"": ""CustomStrict"",
                ""Profiles"": {
                    ""CustomStrict"": {
                        ""Level"": ""Strict"",
                        ""AllowedCommandPatterns"": [""^dotnet run$""],
                        ""BlockedCommandPatterns"": ["".*""],
                        ""AllowedFolders"": [""C:\\Allowed""],
                        ""BlockedFolders"": [""C:\\Blocked""],
                        ""AllowOutsideWorkspace"": false,
                        ""BlockDirectoryTraversal"": true,
                        ""RequireApprovalForSensitiveTools"": true
                    }
                }
            }";

            var config = SecurityPolicyConfig.Parse(json);
            Assert.Equal("CustomStrict", config.ActiveProfile);
            Assert.True(config.Profiles.ContainsKey("CustomStrict"));
            
            var profile = config.Profiles["CustomStrict"];
            Assert.Equal(SecurityProfileLevel.Strict, profile.Level);
            Assert.Contains("^dotnet run$", profile.AllowedCommandPatterns);
            Assert.Contains("C:\\Allowed", profile.AllowedFolders);
            Assert.Contains("C:\\Blocked", profile.BlockedFolders);
            Assert.False(profile.AllowOutsideWorkspace);
            Assert.True(profile.BlockDirectoryTraversal);
        }

        [Fact]
        public void SecurityPolicyConfig_LoadFromFile_Works()
        {
            string tempFile = Path.Combine(_testWorkspaceDir, "security-policy.json");
            string json = @"
            {
                ""ActiveProfile"": ""Permissive"",
                ""Profiles"": {
                    ""Permissive"": {
                        ""Level"": ""Permissive"",
                        ""AllowOutsideWorkspace"": true
                    }
                }
            }";
            File.WriteAllText(tempFile, json);

            var config = SecurityPolicyConfig.LoadFromFile(tempFile);
            Assert.Equal("Permissive", config.ActiveProfile);
            Assert.True(config.Profiles["Permissive"].AllowOutsideWorkspace);
        }

        [Fact]
        public void PermissionEnforcer_PathValidation_StrictProfile()
        {
            var enforcer = new PermissionEnforcer();
            var config = SecurityPolicyConfig.CreateDefault();
            config.ActiveProfile = "Strict";
            PermissionEnforcer.Config = config;

            // Inside workspace path should be allowed
            string insidePath = Path.Combine(_testWorkspaceDir, "src", "file.cs");
            PermissionEnforcer.ActivePath.Value = insidePath;

            var result = enforcer.Evaluate(
                PermissionMode.Prompt,
                "write_file",
                PathSafetyResult.Workspace,
                isSensitiveTool: true,
                new CommandRiskAssessment(CommandRiskLevel.Safe, "Safe", Array.Empty<string>()));
            
            // Inside workspace write requires approval in Prompt mode
            Assert.Equal(PermissionDecision.RequireApproval, result.Decision);

            // Outside workspace path should be denied in Strict
            string outsidePath = Path.Combine(Path.GetTempPath(), "outside.cs");
            PermissionEnforcer.ActivePath.Value = outsidePath;

            result = enforcer.Evaluate(
                PermissionMode.Prompt,
                "write_file",
                PathSafetyResult.Outside,
                isSensitiveTool: true,
                new CommandRiskAssessment(CommandRiskLevel.Safe, "Safe", Array.Empty<string>()));
            
            Assert.Equal(PermissionDecision.Deny, result.Decision);
        }

        [Fact]
        public void PermissionEnforcer_CommandValidation_StrictProfile()
        {
            var enforcer = new PermissionEnforcer();
            var config = SecurityPolicyConfig.CreateDefault();
            config.ActiveProfile = "Strict";
            PermissionEnforcer.Config = config;

            // Allowed command in Strict profile whitelist
            PermissionEnforcer.ActiveCommand.Value = "dotnet build";
            var result = enforcer.Evaluate(
                PermissionMode.Prompt,
                "bash",
                PathSafetyResult.Workspace,
                isSensitiveTool: true,
                new CommandRiskAssessment(CommandRiskLevel.Safe, "Safe", Array.Empty<string>()));
            
            Assert.Equal(PermissionDecision.RequireApproval, result.Decision); // needs approval in Prompt mode for bash tool

            // Blocked command (not whitelisted)
            PermissionEnforcer.ActiveCommand.Value = "rm -rf /";
            result = enforcer.Evaluate(
                PermissionMode.Prompt,
                "bash",
                PathSafetyResult.Workspace,
                isSensitiveTool: true,
                new CommandRiskAssessment(CommandRiskLevel.Dangerous, "Dangerous", Array.Empty<string>()));
            
            Assert.Equal(PermissionDecision.Deny, result.Decision);
        }

        [Fact]
        public void PermissionEnforcer_DirectoryTraversal_Blocked()
        {
            var enforcer = new PermissionEnforcer();
            var config = SecurityPolicyConfig.CreateDefault();
            config.ActiveProfile = "Strict";
            PermissionEnforcer.Config = config;

            // Directory traversal attempt (..)
            PermissionEnforcer.ActivePath.Value = "../etc/passwd";
            var result = enforcer.Evaluate(
                PermissionMode.Prompt,
                "write_file",
                PathSafetyResult.Workspace,
                isSensitiveTool: true,
                new CommandRiskAssessment(CommandRiskLevel.Safe, "Safe", Array.Empty<string>()));
            
            Assert.Equal(PermissionDecision.Deny, result.Decision);
        }

        [Fact]
        public void RedTeam_Harness_Simulation()
        {
            var harness = new RedTeamSecurityHarness();
            var report = harness.RunDiagnostics();

            Assert.NotNull(report);
            
            // In Strict mode, malicious attacks should be blocked
            Assert.True(report.StrictBlocksCount > 0, "Strict profile should block some malicious attacks.");
            Assert.True(report.DevelopmentBlocksCount == 0 || report.DevelopmentBlocksCount < report.StrictBlocksCount,
                "Development profile should block fewer attacks than Strict profile.");
        }
    }

    public class RedTeamSecurityHarness
    {
        public class AttackScenario
        {
            public string Name { get; set; } = "";
            public string ToolName { get; set; } = "";
            public string? Command { get; set; }
            public string? Path { get; set; }
            public PathSafetyResult SafetyResult { get; set; }
            public CommandRiskLevel CommandRisk { get; set; }
            public bool IsSensitive { get; set; }
        }

        public class HarnessReport
        {
            public int StrictBlocksCount { get; set; }
            public int PermissiveBlocksCount { get; set; }
            public int DevelopmentBlocksCount { get; set; }
            public List<string> Logs { get; } = new();
        }

        private readonly List<AttackScenario> _scenarios = new()
        {
            new AttackScenario
            {
                Name = "Directory Traversal Attack",
                ToolName = "write_file",
                Path = "../../etc/passwd",
                SafetyResult = PathSafetyResult.Outside,
                CommandRisk = CommandRiskLevel.Safe,
                IsSensitive = true
            },
            new AttackScenario
            {
                Name = "Command Injection Attack",
                ToolName = "bash",
                Command = "dotnet build && rm -rf /",
                SafetyResult = PathSafetyResult.Workspace,
                CommandRisk = CommandRiskLevel.Dangerous,
                IsSensitive = true
            },
            new AttackScenario
            {
                Name = "Forbidden Command Execution",
                ToolName = "bash",
                Command = "sudo apt-get install malicious-pkg",
                SafetyResult = PathSafetyResult.Workspace,
                CommandRisk = CommandRiskLevel.Dangerous,
                IsSensitive = true
            },
            new AttackScenario
            {
                Name = "Windows System Dir Access",
                ToolName = "view_file",
                Path = @"C:\Windows\System32\cmd.exe",
                SafetyResult = PathSafetyResult.Outside,
                CommandRisk = CommandRiskLevel.Safe,
                IsSensitive = true
            }
        };

        public HarnessReport RunDiagnostics()
        {
            var report = new HarnessReport();
            var enforcer = new PermissionEnforcer();
            var originalConfig = PermissionEnforcer.Config;

            try
            {
                // Test under Strict
                var strictConfig = SecurityPolicyConfig.CreateDefault();
                strictConfig.ActiveProfile = "Strict";
                PermissionEnforcer.Config = strictConfig;

                foreach (var scenario in _scenarios)
                {
                    PermissionEnforcer.ActiveCommand.Value = scenario.Command;
                    PermissionEnforcer.ActivePath.Value = scenario.Path;

                    var eval = enforcer.Evaluate(
                        PermissionMode.Prompt,
                        scenario.ToolName,
                        scenario.SafetyResult,
                        scenario.IsSensitive,
                        new CommandRiskAssessment(scenario.CommandRisk, "Test Risk", Array.Empty<string>()));
                    
                    report.Logs.Add($"[Strict] Scenario '{scenario.Name}' -> Decision: {eval.Decision}, Reason: {eval.Reason}");
                    if (eval.Decision == PermissionDecision.Deny)
                    {
                        report.StrictBlocksCount++;
                    }
                }

                // Test under Permissive
                var permissiveConfig = SecurityPolicyConfig.CreateDefault();
                permissiveConfig.ActiveProfile = "Permissive";
                PermissionEnforcer.Config = permissiveConfig;

                foreach (var scenario in _scenarios)
                {
                    PermissionEnforcer.ActiveCommand.Value = scenario.Command;
                    PermissionEnforcer.ActivePath.Value = scenario.Path;

                    var eval = enforcer.Evaluate(
                        PermissionMode.Prompt,
                        scenario.ToolName,
                        scenario.SafetyResult,
                        scenario.IsSensitive,
                        new CommandRiskAssessment(scenario.CommandRisk, "Test Risk", Array.Empty<string>()));
                    
                    report.Logs.Add($"[Permissive] Scenario '{scenario.Name}' -> Decision: {eval.Decision}, Reason: {eval.Reason}");
                    if (eval.Decision == PermissionDecision.Deny)
                    {
                        report.PermissiveBlocksCount++;
                    }
                }

                // Test under Development
                var devConfig = SecurityPolicyConfig.CreateDefault();
                devConfig.ActiveProfile = "Development";
                PermissionEnforcer.Config = devConfig;

                foreach (var scenario in _scenarios)
                {
                    PermissionEnforcer.ActiveCommand.Value = scenario.Command;
                    PermissionEnforcer.ActivePath.Value = scenario.Path;

                    var eval = enforcer.Evaluate(
                        PermissionMode.Prompt,
                        scenario.ToolName,
                        scenario.SafetyResult,
                        scenario.IsSensitive,
                        new CommandRiskAssessment(scenario.CommandRisk, "Test Risk", Array.Empty<string>()));
                    
                    report.Logs.Add($"[Development] Scenario '{scenario.Name}' -> Decision: {eval.Decision}, Reason: {eval.Reason}");
                    if (eval.Decision == PermissionDecision.Deny)
                    {
                        report.DevelopmentBlocksCount++;
                    }
                }
            }
            finally
            {
                PermissionEnforcer.Config = originalConfig;
                PermissionEnforcer.ActiveCommand.Value = null;
                PermissionEnforcer.ActivePath.Value = null;
            }

            return report;
        }
    }
}
