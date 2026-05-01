using Xunit;
using Claude4Net.SDK;
using Claude4Net.Commands;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    public class D04DiagnosticsTests
    {
        [Fact]
        public void SourceGuard_ShouldFilterSensitivePatterns()
        {
            // Arrange
            string input = "My API key is AIzaSyD-123456789012345678901234567890 and my password=supersecret123. Also Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
            
            // Act
            var result = SourceGuard.Filter(input);
            
            // Assert
            Assert.Contains("****" , result.FilteredText);
            Assert.Contains("password=****", result.FilteredText);
            // SourceGuard replaces the whole Bearer token including "Bearer " with "****" currently
            Assert.DoesNotContain("eyJhbGci", result.FilteredText);
            Assert.DoesNotContain("AIzaSyD", result.FilteredText);
            Assert.DoesNotContain("supersecret123", result.FilteredText);
            Assert.True(result.TotalMatches >= 3);
            Assert.Contains("API Key", result.FoundTypes);
        }

        [Fact]
        public void SecurityUtils_Mask_ShouldBeSafe()
        {
            // Arrange
            string token = "sk-ant-api03-abcdefghijklmnopqrstuvwxyz1234567890";
            
            // Act
            string masked = SecurityUtils.Mask(token);
            
            // Assert
            Assert.Equal("sk-...890", masked);
            Assert.Equal(9, masked.Length);
        }

        [Fact]
        public async Task DoctorCommand_ShouldHideSecrets()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            // Simulate having an API key in environment
            Environment.SetEnvironmentVariable("CLAUDE_API_KEY", "sk-ant-secret-key-1234567890");
            
            // Act
            var command = CommandRegistry.FindCommand("doctor");
            Assert.NotNull(command);
            string result = await command.Handler!("", services);
            
            // Assert
            Assert.Contains("Present", result);
            Assert.DoesNotContain("sk-ant-secret-key-1234567890", result);
        }

        [Fact]
        public async Task EnvCommand_ShouldMaskAllValues()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            Environment.SetEnvironmentVariable("TEST_SECRET_VAR", "secret_value_1234567890");
            
            // Act
            var command = CommandRegistry.FindCommand("env");
            Assert.NotNull(command);
            string result = await command.Handler!("", services);
            
            // Assert
            Assert.Contains("TEST_SECRET_VAR", result);
            Assert.DoesNotContain("secret_value_1234567890", result);
            // It should be either **** (if pattern matched) or sec...890 (if length > 15)
            Assert.True(result.Contains("****") || result.Contains("sec...890"));
        }

        [Fact]
        public void SourceGuard_ShouldHandleNullEmpty()
        {
            Assert.Equal("", SourceGuard.Filter(null).FilteredText);
            Assert.Equal("", SourceGuard.Filter("").FilteredText);
            Assert.Equal("(not set)", SourceGuard.MaskValue(null));
        }
    }
}
