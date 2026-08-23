using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Dashboard.Client.Models;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;
using Claude4Net.Runtime;
using Xunit;
using System.Collections.Generic;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K092DashboardReplayTests : IDisposable
    {
        private readonly string _tempWorkspace;
        private readonly string _originalCwd;
        private readonly string _originalSessionId;
        private readonly string _testSessionId;

        public K092DashboardReplayTests()
        {
            _originalCwd = AppState.CurrentCwd ?? string.Empty;
            _originalSessionId = AppState.SessionId;
            _tempWorkspace = Path.Combine(Path.GetTempPath(), "Claude4Net_K092_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWorkspace);
            AppState.CurrentCwd = _tempWorkspace;
            _testSessionId = "session-k092-" + Guid.NewGuid().ToString("N")[..8];
            AppState.SessionId = _testSessionId;
        }

        public void Dispose()
        {
            AppState.CurrentCwd = _originalCwd;
            AppState.SessionId = _originalSessionId;
            try
            {
                if (Directory.Exists(_tempWorkspace))
                {
                    Directory.Delete(_tempWorkspace, true);
                }
            }
            catch { }
        }

        [Fact]
        public async Task GetSessions_WithEmptyWorkspace_ShouldReturnEmpty()
        {
            var hub = new ControlPlaneHub();
            var sessions = await hub.GetSessions();
            Assert.Empty(sessions);
        }

        [Fact]
        public async Task GetSessions_WithValidSessions_ShouldReturnSessions()
        {
            // Setup a mock session
            var sessionRecord = new AgentSessionRecord
            {
                SessionId = _testSessionId,
                StartTime = DateTime.UtcNow.AddHours(-1),
                Provider = "gemini",
                Model = "gemini-flash",
                PermissionMode = PermissionMode.WorkspaceWrite,
                WorkspacePath = _tempWorkspace,
                Status = "Running",
                Metadata = new Dictionary<string, string> { { "TestKey", "TestVal" } }
            };

            var sessionStore = new AgentSessionStore(_tempWorkspace, _testSessionId);
            await sessionStore.InitializeAsync(sessionRecord);

            var hub = new ControlPlaneHub();
            var sessions = await hub.GetSessions();

            Assert.NotEmpty(sessions);
            var sessionDto = sessions.FirstOrDefault(s => s.SessionId == _testSessionId);
            Assert.NotNull(sessionDto);
            Assert.Equal("gemini", sessionDto.Provider);
            Assert.Equal("gemini-flash", sessionDto.Model);
            Assert.Equal("WorkspaceWrite", sessionDto.PermissionMode);
            Assert.Equal("Running", sessionDto.Status);
            Assert.Equal("TestVal", sessionDto.Metadata["TestKey"]);
        }

        [Fact]
        public async Task GetSessionEvents_WithInvalidSessionId_ShouldReturnEmpty()
        {
            var hub = new ControlPlaneHub();
            var eventsDotDot = await hub.GetSessionEvents("../escape");
            var eventsSlash = await hub.GetSessionEvents("escape/dir");

            Assert.Empty(eventsDotDot);
            Assert.Empty(eventsSlash);
        }

        [Fact]
        public async Task GetSessionEvents_WithValidEvents_ShouldReturnReplayEvents()
        {
            var eventStore = new FileAgentEventStore(_tempWorkspace);
            await eventStore.AppendEventAsync(_testSessionId, new SessionStartedEvent
            {
                Version = 1,
                WorkspacePath = _tempWorkspace,
                Provider = "gemini",
                Model = "gemini-flash"
            });
            await eventStore.AppendEventAsync(_testSessionId, new UserPromptReceivedEvent
            {
                Version = 2,
                Prompt = "Verify Milestone K092"
            });

            var hub = new ControlPlaneHub();
            var events = await hub.GetSessionEvents(_testSessionId);

            Assert.Equal(2, events.Count);
            Assert.Equal("SessionStarted", events[0].EventType);
            Assert.Equal(1, events[0].Version);
            Assert.Contains(_tempWorkspace, events[0].Summary);

            Assert.Equal("UserPromptReceived", events[1].EventType);
            Assert.Equal(2, events[1].Version);
            Assert.Contains("Verify Milestone K092", events[1].Summary);
        }

        [Fact]
        public async Task ReconstructState_ShouldReconstructStateAtStep()
        {
            AppState.CurrentCwd = _tempWorkspace;
            AppState.SessionId = _testSessionId;
            var eventStore = new FileAgentEventStore(_tempWorkspace);
            await eventStore.AppendEventAsync(_testSessionId, new SessionStartedEvent
            {
                Version = 1,
                WorkspacePath = _tempWorkspace,
                Provider = "gemini",
                Model = "gemini-flash"
            });
            await eventStore.AppendEventAsync(_testSessionId, new UserPromptReceivedEvent
            {
                Version = 2,
                Prompt = "Verify Milestone K092"
            });
            await eventStore.AppendEventAsync(_testSessionId, new FinalResponseGeneratedEvent
            {
                Version = 3,
                Response = "Milestone K092 Verified Successfully"
            });

            var hub = new ControlPlaneHub();

            // Reconstruct at step 0 (initial)
            var state0 = await hub.ReconstructState(_testSessionId, 0);
            Assert.Empty(state0.HistoryJson);
            Assert.Equal(string.Empty, state0.CurrentTask);
            Assert.Equal(0, state0.LastVersion);

            // Reconstruct at step 2 (SessionStarted + UserPromptReceived)
            var state2 = await hub.ReconstructState(_testSessionId, 2);
            Assert.Single(state2.HistoryJson); // UserPromptReceived adds 1 history item (role=user, content=prompt)
            Assert.Equal("Verify Milestone K092", state2.CurrentTask);
            Assert.Equal(2, state2.LastVersion);

            // Reconstruct at step 3 (All 3 events)
            var state3 = await hub.ReconstructState(_testSessionId, 3);
            Assert.Equal(2, state3.HistoryJson.Count); // UserPromptReceived + FinalResponseGenerated
            Assert.Equal("Verify Milestone K092", state3.CurrentTask);
            Assert.Equal(3, state3.LastVersion);
        }
    }
}
