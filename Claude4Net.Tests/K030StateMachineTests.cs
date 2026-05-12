using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Tests
{
    public class K030StateMachineTests
    {
        [Fact]
        public void OscillationDetector_ShouldDetectRepeatedTools()
        {
            var detector = new OscillationDetector();
            var events = new List<IAgentEvent>
            {
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-4) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-3) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-2) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-1) }
            };

            bool isOscillating = detector.IsOscillating(events);
            Assert.True(isOscillating);
        }

        [Fact]
        public void OscillationDetector_ShouldNotDetectDiverseTools()
        {
            var detector = new OscillationDetector();
            var events = new List<IAgentEvent>
            {
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-5) },
                new ToolCalledEvent { ToolName = "read_file", Timestamp = DateTime.UtcNow.AddMinutes(-4) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-3) },
                new ToolCalledEvent { ToolName = "write_file", Timestamp = DateTime.UtcNow.AddMinutes(-2) },
                new ToolCalledEvent { ToolName = "ls", Timestamp = DateTime.UtcNow.AddMinutes(-1) }
            };

            bool isOscillating = detector.IsOscillating(events);
            Assert.False(isOscillating);
        }

        [Fact]
        public void AgentStateModels_ShouldTrackAttempts()
        {
            var state = new AgentRunStateModel
            {
                SessionId = "test-session",
                CurrentState = AgentRunState.ExecutingTool
            };

            state.Attempts.Add(new AttemptRecord
            {
                Sequence = 1,
                Goal = "Test Goal",
                IsSuccess = true,
                StartedAt = DateTime.UtcNow,
                DurationMs = 500
            });

            Assert.Single(state.Attempts);
            Assert.Equal("Test Goal", state.Attempts[0].Goal);
            Assert.True(state.Attempts[0].IsSuccess);
        }
    }
}
