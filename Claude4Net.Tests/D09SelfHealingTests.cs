using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Services;
using System.IO;
using System;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class D09SelfHealingTests
    {
        private readonly SelfHealingService _service = SelfHealingService.Instance;

        [Fact]
        public void ErrorClassifier_ShouldCategorizeCorrectly_Extended()
        {
            Assert.Equal(ErrorCategory.QuotaError, Claude4Net.SDK.ErrorClassifier.Classify("gemini", "Resource has been exhausted (e.g. check quota)."));
            Assert.Equal(ErrorCategory.NetworkError, Claude4Net.SDK.ErrorClassifier.Classify("claude", "The connection was reset by the peer."));
            Assert.Equal(ErrorCategory.TimeoutError, Claude4Net.SDK.ErrorClassifier.Classify("tool", "Operation timed out after 30 seconds."));
            Assert.Equal(ErrorCategory.LogicError, Claude4Net.SDK.ErrorClassifier.Classify("pandas", "Invalid argument: column 'X' does not exist."));
            Assert.Equal(ErrorCategory.BuildError, Claude4Net.SDK.ErrorClassifier.Classify("bash", "error CS0103: The name 'Color' does not exist in the current context"));
        }

        [Fact]
        public void ErrorClassifier_ShouldReturnRecommendedPolicy()
        {
            var quotaPolicy = Claude4Net.SDK.ErrorClassifier.GetRecommendedPolicy(ErrorCategory.QuotaError);
            Assert.Equal(RetryStrategy.ExponentialBackoff, quotaPolicy.Strategy);
            Assert.Equal(5000, quotaPolicy.InitialDelayMs);

            var networkPolicy = Claude4Net.SDK.ErrorClassifier.GetRecommendedPolicy(ErrorCategory.NetworkError);
            Assert.Equal(RetryStrategy.ExponentialBackoff, networkPolicy.Strategy);
            Assert.Equal(3, networkPolicy.MaxRetries);
        }

        [Fact]
        public void SelfHealingService_ShouldIncludeRetryPoliciesInGuide()
        {
            _service.UpdateGuide("Reflecting on recent quota errors.");

            string guide = _service.GetGuide();
            Assert.Contains("## Recommended Retry Policies", guide);
            Assert.Contains("QuotaError", guide);
            Assert.Contains("ExponentialBackoff", guide);
        }

        [Fact]
        public async Task SelfHealingService_Pruning_ShouldWork()
        {
            await _service.PruneTrajectoriesAsync(7);
        }

        [Fact]
        public void SelfHealingService_ShouldCreateAndMaskGuide()
        {
            string summary = "Test failure in bash tool due to permission.";
            _service.UpdateGuide(summary);

            string guide = _service.GetGuide();
            Assert.Contains("# SELF_HEAL_GUIDE", guide);
            Assert.Contains("Test failure in bash tool", guide);
        }

        [Fact]
        public void SystemPromptBuilder_ShouldIncludeGuideIfExists()
        {
            string originalDir = AppState.SystemBaseDir;
            string tempBase = Path.Combine(Path.GetTempPath(), "D09SelfHealing_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempBase);
            try
            {
                AppState.SystemBaseDir = tempBase;
                string guidePath = Path.Combine(tempBase, "SELF_HEAL_GUIDE.md");
                File.WriteAllText(guidePath, "CUSTOM_SELF_HEAL_INSTRUCTION");

                var builder = new SystemPromptBuilder();

                string prompt = builder.Build("gemini");

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
