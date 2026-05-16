using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Claude4Net.Cli.Ui.Approval;

namespace Claude4Net.Cli.Ui.Approval
{
    /// <summary>
    /// Queue for managing pending approval requests in Lumen UI.
    /// Bridges the asynchronous tool execution with the interactive UI loop.
    /// </summary>
    public sealed class ApprovalQueue
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ApprovalDialogAction>> _pendingRequests = new();

        /// <summary>
        /// Registers a new approval request and returns a task that completes when the user makes a decision.
        /// </summary>
        public Task<ApprovalDialogAction> EnqueueAsync(string requestId)
        {
            var tcs = new TaskCompletionSource<ApprovalDialogAction>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[requestId] = tcs;
            return tcs.Task;
        }

        /// <summary>
        /// Signals that a decision has been made for a specific request.
        /// </summary>
        public void Resolve(string requestId, ApprovalDialogAction action)
        {
            if (_pendingRequests.TryRemove(requestId, out var tcs))
            {
                tcs.TrySetResult(action);
            }
        }

        /// <summary>
        /// Cancels all pending requests, usually called when the application or run is terminating.
        /// </summary>
        public void CancelAll()
        {
            foreach (var key in _pendingRequests.Keys)
            {
                if (_pendingRequests.TryRemove(key, out var tcs))
                {
                    tcs.TrySetCanceled();
                }
            }
        }
    }
}
