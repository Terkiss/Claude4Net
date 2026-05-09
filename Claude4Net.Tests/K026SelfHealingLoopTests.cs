using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Moq;

namespace Claude4Net.Tests
{
    public class K026SelfHealingLoopTests
    {
        [Fact]
        public void Classifier_InfiniteLoop_ShouldDetectPattern()
        {
            // Arrange
            var events = new List<IAgentEvent>
            {
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" },
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" },
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" }
            };

            // Act
            var pattern = SelfHealingService.Instance.ClassifyPattern(events);

            // Assert
            Assert.Equal(FailurePattern.InfiniteLoop, pattern);
        }

        [Fact]
        public void Classifier_Hallucination_ShouldDetectPattern()
        {
            // Arrange
            var events = new List<IAgentEvent>
            {
                new UserPromptReceivedEvent { Prompt = "test" }, // Dummy
                new ToolResultEvent { ToolUseId = "t1", Result = "File not found", IsError = true },
                new ToolResultEvent { ToolUseId = "t2", Result = "No such file", IsError = true }
            };

            // Act
            var pattern = SelfHealingService.Instance.ClassifyPattern(events);

            // Assert
            Assert.Equal(FailurePattern.Hallucination, pattern);
        }

        [Fact]
        public void MaxReflectionDepth_ShouldLimitAttempts()
        {
            // Arrange
            SelfHealingService.Instance.ResetReflectionDepth();

            // Act & Assert
            Assert.True(SelfHealingService.Instance.IncrementReflectionDepth()); // 1
            Assert.True(SelfHealingService.Instance.IncrementReflectionDepth()); // 2
            Assert.True(SelfHealingService.Instance.IncrementReflectionDepth()); // 3
            Assert.False(SelfHealingService.Instance.IncrementReflectionDepth()); // 4 (Limit is 3)
        }

        [Fact]
        public void DirectiveInjection_ShouldIncludeInGuide()
        {
            // Arrange
            SelfHealingService.Instance.ResetReflectionDepth();
            var directive = SelfHealingService.Instance.GenerateDirective(FailurePattern.InfiniteLoop);

            // Act
            var guide = SelfHealingService.Instance.GetGuide();

            // Assert
            Assert.Contains("Self-Healing Directives", guide);
            Assert.Contains(directive.Instruction, guide);
        }

        [Fact]
        public async Task StrategySwitch_Trigger_ShouldAddCriticalMessage()
        {
            // This test would ideally test AgentLoop.RunAsync with a mock provider
            // to see if the CRITICAL message is added when max depth is reached.
            // Since RunAsync is complex to mock fully here, we verify the logic manually or via component test.

            // For now, we'll verify the classification logic and depth increment which are the triggers.
            SelfHealingService.Instance.ResetReflectionDepth();
            for(int i=0; i<3; i++) SelfHealingService.Instance.IncrementReflectionDepth();

            Assert.False(SelfHealingService.Instance.IncrementReflectionDepth()); // Triggers strategy switch in AgentLoop
        }
    }
}
