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
            string keyName = "CLAUDE_API_KEY";
            string? originalValue = Environment.GetEnvironmentVariable(keyName);
            try
            {
                // Simulate having an API key in environment
                Environment.SetEnvironmentVariable(keyName, "sk-ant-secret-key-1234567890");
                
                // Act
                var command = CommandRegistry.FindCommand("doctor");
                Assert.NotNull(command);
                string result = await command.Handler!("", services);
                
                // Assert
                Assert.Contains("Present", result);
                Assert.DoesNotContain("sk-ant-secret-key-1234567890", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(keyName, originalValue);
            }
        }

        [Fact]
        public async Task EnvCommand_ShouldMaskAllValues()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            string keyName = "AAAA_TEST_SECRET_VAR";
            string? originalValue = Environment.GetEnvironmentVariable(keyName);
            try
            {
                Environment.SetEnvironmentVariable(keyName, "secret_value_1234567890");
                
                // Act
                var command = CommandRegistry.FindCommand("env");
                Assert.NotNull(command);
                string result = await command.Handler!("", services);
                
                // Assert
                Assert.Contains("AAAA_TEST_SECRET_VAR", result);
                Assert.DoesNotContain("secret_value_1234567890", result);
                Assert.Contains("sec...890", result);
                Assert.Contains("Use /env all", result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(keyName, originalValue);
            }
        }

        [Fact]
        public async Task EnvCommand_ShouldSupportAllArgument()
        {
            // Arrange
            var services = new ServiceCollection().BuildServiceProvider();
            
            // Act
            var command = CommandRegistry.FindCommand("env");
            Assert.NotNull(command);
            string result = await command.Handler!("all", services);
            
            // Assert
            Assert.Contains("Environment Variables (All Values Source-Guarded)", result);
            Assert.DoesNotContain("Use /env all", result);
        }

        [Fact]
        public void SourceGuard_ShouldNotMaskPlainLongText()
        {
            // Arrange
            string input = "ThisIsAPlainLongIdentifierForDocumentationOnly";
            
            // Act
            var result = SourceGuard.Filter(input);
            string masked = SourceGuard.MaskValue(input);
            
            // Assert
            Assert.True(result.IsClean);
            Assert.Equal(input, result.FilteredText);
            Assert.Equal(input, masked);
        }

        [Fact]
        public void SourceGuard_ShouldMaskSensitiveKeyContext()
        {
            Assert.Equal("lon...lue", SourceGuard.MaskValue("long-but-plain-value", "CUSTOM_TOKEN"));
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
