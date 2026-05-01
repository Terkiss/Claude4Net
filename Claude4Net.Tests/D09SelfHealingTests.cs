using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using System.IO;
using System;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D09SelfHealingTests
    {
        [Fact]
        public void ErrorClassifier_ShouldCategorizeCorrectly()
        {
            Assert.Equal(ErrorCategory.PathError, ErrorClassifier.Classify("file_read", "Could not find part of the path 'C:\\invalid'"));
            Assert.Equal(ErrorCategory.PermissionError, ErrorClassifier.Classify("bash", "Access to the path is denied."));
            Assert.Equal(ErrorCategory.BuildError, ErrorClassifier.Classify("bash", "Build failed. CS0246: The type or namespace name 'X' could not be found"));
            Assert.Equal(ErrorCategory.TestError, ErrorClassifier.Classify("bash", "Test failed: Assert.Equal() Failure"));
            Assert.Equal(ErrorCategory.ProviderError, ErrorClassifier.Classify("claude", "Rate limit exceeded"));
        }

        [Fact]
        public void SelfHealingService_ShouldCreateAndMaskGuide()
        {
            var service = SelfHealingService.Instance;
            string summary = "Test failure in bash tool due to permission.";
            service.UpdateGuide(summary);

            string guide = service.GetGuide();
            Assert.Contains("# SELF_HEAL_GUIDE", guide);
            Assert.Contains("Test failure in bash tool", guide);
        }

        [Fact]
        public void SystemPromptBuilder_ShouldIncludeGuideIfExists()
        {
            // Arrange
            string originalDir = AppState.SystemBaseDir;
            string tempBase = Path.Combine(Path.GetTempPath(), "D09SelfHealing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBase);
            try
            {
                AppState.SystemBaseDir = tempBase;
                string guidePath = Path.Combine(tempBase, "SELF_HEAL_GUIDE.md");
                File.WriteAllText(guidePath, "CUSTOM_SELF_HEAL_INSTRUCTION");

                var builder = new SystemPromptBuilder();

                // Act
                string prompt = builder.Build("gemini");

                // Assert
                Assert.Contains("## 🩹 Self-Healing Guide", prompt);
                Assert.Contains("CUSTOM_SELF_HEAL_INSTRUCTION", prompt);
            }
            finally
            {
                AppState.SystemBaseDir = originalDir;
                if (Directory.Exists(tempBase)) Directory.Delete(tempBase, true);
            }
        }
    }
}
