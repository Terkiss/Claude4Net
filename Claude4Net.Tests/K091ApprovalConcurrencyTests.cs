using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.AspNetCore.SignalR;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Approval;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.Runtime;
using Claude4Net.SDK;
using Spectre.Console;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K091ApprovalConcurrencyTests : IDisposable
    {
        public K091ApprovalConcurrencyTests()
        {
            IdempotentApprovalEngine.Reset();
        }

        public void Dispose()
        {
            IdempotentApprovalEngine.Reset();
        }

        [Fact]
        public void IdempotentApprovalEngine_ShouldRegisterAndRetrieveRequest()
        {
            string reqId = "req-1";
            IdempotentApprovalEngine.RegisterRequest(reqId, "test-tool");

            var decision = IdempotentApprovalEngine.GetDecision(reqId);
            Assert.Null(decision); // Pending
        }

        [Fact]
        public void IdempotentApprovalEngine_ShouldBeIdempotentForSameDecisions()
        {
            string reqId = "req-2";
            IdempotentApprovalEngine.RegisterRequest(reqId, "test-tool");

            bool success1 = IdempotentApprovalEngine.TryRegisterDecision(reqId, true, "approved by user", out var err1);
            bool success2 = IdempotentApprovalEngine.TryRegisterDecision(reqId, true, "approved again", out var err2);

            Assert.True(success1);
            Assert.True(success2);
            Assert.Null(err1);
            Assert.Null(err2);
            Assert.True(IdempotentApprovalEngine.GetDecision(reqId));
        }

        [Fact]
        public void IdempotentApprovalEngine_ShouldRejectConflictingDecisions()
        {
            string reqId = "req-3";
            IdempotentApprovalEngine.RegisterRequest(reqId, "test-tool");

            bool success1 = IdempotentApprovalEngine.TryRegisterDecision(reqId, true, "approved first", out var err1);
            bool success2 = IdempotentApprovalEngine.TryRegisterDecision(reqId, false, "reject second", out var err2);

            Assert.True(success1);
            Assert.False(success2);
            Assert.Null(err1);
            Assert.NotNull(err2);
            Assert.Contains("Conflicting decision", err2);
            Assert.True(IdempotentApprovalEngine.GetDecision(reqId));
        }

        [Fact]
        public async Task IdempotentApprovalEngine_ConcurrencyTest_SameDecision()
        {
            string reqId = "req-concurrent-same";
            IdempotentApprovalEngine.RegisterRequest(reqId, "test-tool");

            int numTasks = 50;
            var tasks = new List<Task<bool>>();

            for (int i = 0; i < numTasks; i++)
            {
                tasks.Add(Task.Run(() =>
                {
                    return IdempotentApprovalEngine.TryRegisterDecision(reqId, true, "approved concurrently", out _);
                }));
            }

            var results = await Task.WhenAll(tasks);

            // All should succeed because same decision (true) is idempotent
            Assert.All(results, Assert.True);
            Assert.True(IdempotentApprovalEngine.GetDecision(reqId));
        }

        [Fact]
        public async Task IdempotentApprovalEngine_ConcurrencyTest_ConflictingDecisions()
        {
            string reqId = "req-concurrent-conflict";
            IdempotentApprovalEngine.RegisterRequest(reqId, "test-tool");

            int numTasks = 50;
            var tasks = new List<Task<(bool Success, string Error)>>();

            for (int i = 0; i < numTasks; i++)
            {
                bool decisionValue = (i % 2 == 0); // Alternate true and false
                tasks.Add(Task.Run(() =>
                {
                    bool ok = IdempotentApprovalEngine.TryRegisterDecision(reqId, decisionValue, "decision concurrently", out var err);
                    return (ok, err ?? "");
                }));
            }

            var results = await Task.WhenAll(tasks);

            // The very first decision taken wins. The others with the same value succeed (idempotent),
            // while the ones with conflicting value must fail.
            bool finalDecision = IdempotentApprovalEngine.GetDecision(reqId) ?? throw new Exception("Decision should not be null");

            int successes = 0;
            int conflicts = 0;

            foreach (var res in results)
            {
                if (res.Success)
                {
                    successes++;
                    Assert.Equal("", res.Error);
                }
                else
                {
                    conflicts++;
                    Assert.Contains("Conflicting decision", res.Error);
                }
            }

            Assert.True(successes > 0);
            Assert.True(conflicts > 0);
        }

        [Fact]
        public async Task LumenApprovalHandler_ShouldResolveViaResolver()
        {
            // Arrange
            var mockObserver = new Mock<ILumenFrameBuilder>();
            var rendererMock = new Mock<ILumenTerminalRenderer>();
            var state = new LumenState();
            var observer = new LumenRunObserver(new LumenRenderer(AnsiConsole.Console, mockObserver.Object, rendererMock.Object), state);
            var queue = new ApprovalQueue();
            var handler = new LumenApprovalHandler(observer, queue);

            // Act - start waiting for approval in background
            var approvalTask = handler.RequestApprovalAsync("sensitive-tool", "arg1");

            // Give a tiny moment for Task.Run/background initialization if any, though it starts synchronously up to await
            await Task.Delay(50);

            // Retrieve the registered request ID from the engine
            // Since we know the request registers under Guid.NewGuid().ToString().Substring(0, 8)
            // and we reset the engine before the test, we can grab the active request ID.
            string reqId = observer.State.ApprovalDialog.RequestId;
            if (string.IsNullOrEmpty(reqId))
            {
                // Fallback to getting it from the state dialog
                reqId = observer.State.ApprovalDialog.RequestId;
            }

            // Simulate external channel resolving the request ID
            bool decisionSet = IdempotentApprovalEngine.TryRegisterDecision(reqId, true, "WebUI Approve", out _);
            Assert.True(decisionSet);

            // Assert
            bool approved = await approvalTask;
            Assert.True(approved);
            Assert.True(IdempotentApprovalEngine.GetDecision(reqId));
        }

        [Fact]
        public async Task AgentHub_ShouldRegisterDecisionAndThrowOnConflict()
        {
            // Arrange
            var mockClients = new Mock<IHubCallerClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

            var hub = new AgentHub
            {
                Clients = mockClients.Object
            };

            string reqId = "hub-req-1";
            IdempotentApprovalEngine.RegisterRequest(reqId, "tool");

            // Act - First response
            await hub.RespondToApproval(reqId, true, "Approved");
            Assert.True(IdempotentApprovalEngine.GetDecision(reqId));

            // Act & Assert - Duplicate same response (should succeed idempotently)
            await hub.RespondToApproval(reqId, true, "Approved again");

            // Act & Assert - Conflicting response (should throw HubException)
            var ex = await Assert.ThrowsAsync<HubException>(() => hub.RespondToApproval(reqId, false, "Rejected"));
            Assert.Contains("Conflicting decision", ex.Message);
        }
    }
}
