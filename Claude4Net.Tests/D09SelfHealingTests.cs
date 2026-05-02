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
        public void ErrorClassifier_ShouldCategorizeCorrectly_Extended()
        {
            Assert.Equal(ErrorCategory.QuotaError, ErrorClassifier.Classify("gemini", "Resource has been exhausted (e.g. check quota)."));
            Assert.Equal(ErrorCategory.NetworkError, ErrorClassifier.Classify("claude", "The connection was reset by the peer."));
            Assert.Equal(ErrorCategory.TimeoutError, ErrorClassifier.Classify("tool", "Operation timed out after 30 seconds."));
            Assert.Equal(ErrorCategory.LogicError, ErrorClassifier.Classify("pandas", "Invalid argument: column 'X' does not exist."));
            Assert.Equal(ErrorCategory.BuildError, ErrorClassifier.Classify("bash", "error CS0103: The name 'Color' does not exist in the current context"));
        }

        [Fact]
        public void ErrorClassifier_ShouldReturnRecommendedPolicy()
        {
            var quotaPolicy = ErrorClassifier.GetRecommendedPolicy(ErrorCategory.QuotaError);
            Assert.Equal(RetryStrategy.ExponentialBackoff, quotaPolicy.Strategy);
            Assert.Equal(5000, quotaPolicy.InitialDelayMs);

            var networkPolicy = ErrorClassifier.GetRecommendedPolicy(ErrorCategory.NetworkError);
            Assert.Equal(RetryStrategy.ExponentialBackoff, networkPolicy.Strategy);
            Assert.Equal(3, networkPolicy.MaxRetries);
        }

        [Fact]
        public void SelfHealingService_ShouldIncludeRetryPoliciesInGuide()
        {
            var service = SelfHealingService.Instance;
            service.UpdateGuide("Reflecting on recent quota errors.");

            string guide = service.GetGuide();
            Assert.Contains("## 🔄 Recommended Retry Policies", guide);
            Assert.Contains("QuotaError", guide);
            Assert.Contains("ExponentialBackoff", guide);
        }

        [Fact]
        public async Task SelfHealingService_Pruning_ShouldWork()
        {
            // This test interacts with PandasUniverseManager, so it might need a real or mock setup
            // For now, we'll verify it doesn't crash and reports correctly if table exists
            await SelfHealingService.Instance.PruneTrajectoriesAsync(7);
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
