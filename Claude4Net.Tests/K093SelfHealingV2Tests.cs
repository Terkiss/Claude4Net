using System;
using System.Collections.Generic;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K093SelfHealingV2Tests
    {
        [Fact]
        public void ErrorClassifier_ShouldRefineCategoriesCorrectly()
        {
            // Json Schema Mismatch
            var jsonErrorCat = Claude4Net.Runtime.ErrorClassifier.Classify("tool", "JSON Schema mismatch: required parameter 'name' is missing.");
            Assert.Equal(RefinedErrorCategory.JsonSchemaMismatch, jsonErrorCat);

            // Rate Limit
            var rateLimitCat = Claude4Net.Runtime.ErrorClassifier.Classify("claude", "Rate limit exceeded. Too many requests. HTTP 429.");
            Assert.Equal(RefinedErrorCategory.RateLimit, rateLimitCat);

            // Context Limit Over
            var contextLimitCat = Claude4Net.Runtime.ErrorClassifier.Classify("gemini", "Maximum tokens exceeded. Context limit reached.");
            Assert.Equal(RefinedErrorCategory.ContextLimitOver, contextLimitCat);

            // Symlink Escape Violation
            var symlinkCat = Claude4Net.Runtime.ErrorClassifier.Classify("bash", "Security Exception: Symlink escape path detected. Access outside workspace denied.");
            Assert.Equal(RefinedErrorCategory.SymlinkEscapeViolation, symlinkCat);
        }

        [Fact]
        public void SelfHealingService_RecommendRecovery_ShouldReturnAppropriatePrescriptions()
        {
            var service = SelfHealingService.Instance;

            // 1. JSON Schema Mismatch
            var jsonPrescription = service.RecommendRecovery(RefinedErrorCategory.JsonSchemaMismatch, "tool", "error");
            Assert.Contains("JSON Schema mismatch", jsonPrescription.SuggestedPromptAdjustment);
            Assert.Equal(RetryStrategy.Immediate, jsonPrescription.RetryPolicy!.Strategy);

            // 2. Rate Limit (Alternative Routing)
            var prevModel = AppState.ActiveModel;
            try
            {
                AppState.ActiveModel = "gemini-1.5-pro";
                var ratePrescription = service.RecommendRecovery(RefinedErrorCategory.RateLimit, "claude", "error");
                Assert.Equal("gemini-1.5-flash", ratePrescription.SuggestedModel);
                Assert.Equal(RetryStrategy.ExponentialBackoff, ratePrescription.RetryPolicy!.Strategy);
            }
            finally
            {
                AppState.ActiveModel = prevModel;
            }

            // 3. Symlink Escape
            var symlinkPrescription = service.RecommendRecovery(RefinedErrorCategory.SymlinkEscapeViolation, "tool", "error");
            Assert.Contains("Path safety restriction", symlinkPrescription.SuggestedPromptAdjustment);
            Assert.Equal(RetryStrategy.None, symlinkPrescription.RetryPolicy!.Strategy);
        }

        [Fact]
        public void SelfHealingService_ClassifyPattern_ShouldTriggerOnRecoveryPrescribedEvent()
        {
            var service = SelfHealingService.Instance;
            RecoveryPrescription? receivedPrescription = null;

            Action<RecoveryPrescription> handler = p => { receivedPrescription = p; };
            SelfHealingService.OnRecoveryPrescribed += handler;

            try
            {
                var events = new List<IAgentEvent>
                {
                    new ToolCalledEvent { ToolUseId = "t1", ToolName = "ls", Arguments = "src" },
                    new ToolResultEvent { ToolUseId = "t1", Result = "JSON Schema mismatch: required field missing", IsError = true }
                };

                // ClassifyPattern expects at least 3 events to process
                events.Insert(0, new UserPromptReceivedEvent { Prompt = "Start" });

                var pattern = service.ClassifyPattern(events);

                Assert.Equal(FailurePattern.ToolUsageError, pattern);
                Assert.NotNull(receivedPrescription);
                Assert.Equal(RefinedErrorCategory.JsonSchemaMismatch, receivedPrescription!.Category);
            }
            finally
            {
                SelfHealingService.OnRecoveryPrescribed -= handler;
            }
        }
    }
}
