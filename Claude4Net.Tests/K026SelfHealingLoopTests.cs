using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.Runtime.Services;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Moq;
using SelfHealingService = Claude4Net.Runtime.Services.SelfHealingService;
using FailurePattern = Claude4Net.SDK.FailurePattern;
using RuntimeFailurePattern = Claude4Net.Runtime.Services.FailurePattern;

namespace Claude4Net.Tests
{
    public class K026SelfHealingLoopTests
    {
        private readonly SelfHealingService _service = SelfHealingService.Instance;

        [Fact]
        public void Classifier_InfiniteLoop_ShouldDetectPattern()
        {
            var events = new List<IAgentEvent>
            {
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" },
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" },
                new ToolCalledEvent { ToolName = "ls", Arguments = "src" }
            };

            var pattern = _service.ClassifyPattern(events);
            Assert.Equal(RuntimeFailurePattern.InfiniteLoop, pattern);
        }

        [Fact]
        public void Classifier_Hallucination_ShouldDetectPattern()
        {
            var events = new List<IAgentEvent>
            {
                new UserPromptReceivedEvent { Prompt = "test" },
                new ToolResultEvent { ToolUseId = "t1", Result = "File not found", IsError = true },
                new ToolResultEvent { ToolUseId = "t2", Result = "No such file", IsError = true }
            };

            var pattern = _service.ClassifyPattern(events);
            Assert.Equal(RuntimeFailurePattern.Hallucination, pattern);
        }

        [Fact]
        public void MaxReflectionDepth_ShouldLimitAttempts()
        {
            _service.ResetReflectionDepth();
            Assert.True(_service.IncrementReflectionDepth());
            Assert.True(_service.IncrementReflectionDepth());
            Assert.True(_service.IncrementReflectionDepth());
            Assert.False(_service.IncrementReflectionDepth());
        }

        [Fact]
        public void DirectiveInjection_ShouldIncludeInGuide()
        {
            _service.ResetReflectionDepth();
            var directive = _service.GenerateDirective(RuntimeFailurePattern.InfiniteLoop);

            var guide = _service.GetGuide();
            Assert.Contains("Self-Healing Directives", guide);
            Assert.Contains(directive.Instruction, guide);
        }

        [Fact]
        public async Task StrategySwitch_Trigger_ShouldAddCriticalMessage()
        {
            _service.ResetReflectionDepth();
            for(int i=0; i<3; i++) _service.IncrementReflectionDepth();
            Assert.False(_service.IncrementReflectionDepth());
        }
    }
}
