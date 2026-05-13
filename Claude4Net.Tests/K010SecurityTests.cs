using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.Commands;
using TeruTeruPandas.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K010SecurityTests : IDisposable
    {
        private readonly string _originalBaseDir;

        public K010SecurityTests()
        {
            _originalBaseDir = AppState.SystemBaseDir;
            AppState.SystemBaseDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "K010Tests_" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(AppState.SystemBaseDir);

            // Ensure DB and tables exist
            PandasUniverseManager.Instance.EnsureBaselineTablesAsync().Wait();
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(AppState.SystemBaseDir))
            {
                // try { System.IO.Directory.Delete(AppState.SystemBaseDir, true); } catch { }
            }
            AppState.SystemBaseDir = _originalBaseDir;
        }

        [Fact]
        public async Task ToolOrchestrator_AuditLogging_Works()
        {
            var originalMode = AppState.CurrentPermissionMode;
            try
            {
                AppState.CurrentPermissionMode = PermissionMode.Prompt;

                // 1. Arrange: Setup Mock Tool
                var mockTool = new Mock<ITool>();
                mockTool.Setup(t => t.Name).Returns("sensitive_test_tool");
                mockTool.Setup(t => t.ExecuteAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<System.Threading.CancellationToken>()))
                        .ReturnsAsync("Tool Executed");

                var services = new ServiceCollection();
                var sp = services.BuildServiceProvider();
                var orchestrator = new ToolOrchestrator(new[] { mockTool.Object }, null, sp);

                // 2. Act: Execute sensitive tool
                var request = new ToolUseRequest { Id = "test-1", Name = "sensitive_test_tool", Input = new Dictionary<string, object> { ["path"] = "test.txt" } };
                await orchestrator.ExecuteToolAsync(request, new object());

                // Wait a bit for async logging
                await Task.Delay(500);

                // 3. Assert: Check audit_logs table
                await PandasUniverseManager.Instance.ExecuteAsync(u =>
                {
                    var df = u.GetTableOrThrow("audit_logs");
                    Assert.True(df.RowCount > 0);

                    // Find our tool log. K015 policy denies sensitive tools without an approval handler.
                    bool found = false;
                    for (int i = 0; i < df.RowCount; i++)
                    {
                        if (df["ToolName"].GetValue(i)?.ToString() == "sensitive_test_tool")
                        {
                            found = true;
                            var status = df["Status"].GetValue(i)?.ToString() ?? "";
                            Assert.StartsWith("Denied (No Handler)", status);
                            break;
                        }
                    }
                    Assert.True(found, "Audit log for sensitive_test_tool not found.");
                    return null!;
                });
            }
            finally
            {
                AppState.CurrentPermissionMode = originalMode;
            }
        }

        [Fact]
        public void SourceGuard_AdvancedMasking_Works()
        {
            // AWS Key
            var awsRes = SourceGuard.Filter("My key is AKIA1234567890ABCDEF");
            Assert.Contains("****", awsRes.FilteredText);

            // SSH Key
            var sshKey = "-----BEGIN RSA PRIVATE KEY-----\nMIIEpAIBAAKCAQEA7...\n-----END RSA PRIVATE KEY-----";
            var sshRes = SourceGuard.Filter(sshKey);
            Assert.Equal("****", sshRes.FilteredText);

            // LookSensitiveKey enhancement
            Assert.True(SourceGuard.LooksSensitiveKey("DB_PASSWORD"));
            Assert.True(SourceGuard.LooksSensitiveKey("LICENSE_KEY"));
            Assert.True(SourceGuard.LooksSensitiveKey("PRIVATE_CERT"));

            // MaskValue with key name
            var masked = SourceGuard.MaskValue("secret123", "AUTH_TOKEN");
            Assert.NotEqual("secret123", masked);
        }

        [Fact]
        public async Task Command_DoctorAndAudit_SmokeTest()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISmartRouter>(new SmartRouter());
            var sp = services.BuildServiceProvider();

            // Doctor
            var doctorCmd = CommandRegistry.FindCommand("doctor");
            Assert.NotNull(doctorCmd);
            var doctorRes = await doctorCmd.Handler!("", sp);
            Assert.Contains("Diagnostics", doctorRes);
            Assert.Contains("Security Audit", doctorRes);

            // Audit
            var auditCmd = CommandRegistry.FindCommand("audit");
            Assert.NotNull(auditCmd);
            var auditRes = await auditCmd.Handler!("5", sp);
            Assert.NotNull(auditRes);
        }
    }
}
