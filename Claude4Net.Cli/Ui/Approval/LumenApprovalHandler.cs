using System;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Runtime;

namespace Claude4Net.Cli.Ui.Approval
{
    /// <summary>
    /// Approval handler for Project Lumen.
    /// Redirects tool approval requests to the interactive Lumen UI via events and waits for user decision.
    /// </summary>
    public sealed class LumenApprovalHandler : IRichApprovalHandler
    {
        private readonly LumenRunObserver _observer;
        private readonly ApprovalQueue _queue;

        public LumenApprovalHandler(LumenRunObserver observer, ApprovalQueue queue)
        {
            _observer = observer;
            _queue = queue;
        }

        public async Task<bool> RequestApprovalAsync(string tool, string args)
        {
            var requestId = Guid.NewGuid().ToString().Substring(0, 8);

            IdempotentApprovalEngine.RegisterRequest(requestId, tool);

            // Open UI Dialog via Event
            _observer.UpdateState(new ApprovalDialogOpenedEvent(
                requestId,
                $"Approval Required: {tool}",
                $"The agent wants to execute {tool} with arguments: {args}",
                "Medium",
                ""
            ));

            Action<string, bool, string?> resolver = (id, approved, reason) =>
            {
                if (id == requestId)
                {
                    var action = approved ? ApprovalDialogAction.Approve : ApprovalDialogAction.Deny;
                    _queue.Resolve(requestId, action);
                }
            };

            IdempotentApprovalEngine.RegisterResolver(resolver);

            try
            {
                var existingDecision = IdempotentApprovalEngine.GetDecision(requestId);
                ApprovalDialogAction action;

                if (existingDecision.HasValue)
                {
                    action = existingDecision.Value ? ApprovalDialogAction.Approve : ApprovalDialogAction.Deny;
                }
                else
                {
                    action = await _queue.EnqueueAsync(requestId);

                    bool approved = action == ApprovalDialogAction.Approve;
                    if (!IdempotentApprovalEngine.TryRegisterDecision(requestId, approved, null, out var error))
                    {
                        if (error != null)
                        {
                            Console.WriteLine($"[ERROR] Conflict detected in LumenApprovalHandler: {error}");
                            throw new InvalidOperationException(error);
                        }
                    }
                }

                // Log final approval result as a durable NoticeCell
                string statusStr = action == ApprovalDialogAction.Approve ? "APPROVED" : (action == ApprovalDialogAction.Deny ? "DENIED" : "CANCELLED");
                string level = action == ApprovalDialogAction.Approve ? "Success" : "Warning";
                _observer.UpdateState(new NoticeReceivedEvent($"[Approval] {tool} -> {statusStr}", level));

                return action == ApprovalDialogAction.Approve;
            }
            finally
            {
                IdempotentApprovalEngine.UnregisterResolver(resolver);
            }
        }

        public async Task<bool> RequestApprovalWithDiffAsync(string tool, string args, FileDiffPreview diff)
        {
            var requestId = Guid.NewGuid().ToString().Substring(0, 8);

            IdempotentApprovalEngine.RegisterRequest(requestId, tool);

            // Open UI Dialog via Event with Diff
            _observer.UpdateState(new ApprovalDialogOpenedEvent(
                requestId,
                $"File Edit Approval: {tool}",
                $"The agent wants to modify a file.",
                "High",
                diff.DiffContent
            ));

            Action<string, bool, string?> resolver = (id, approved, reason) =>
            {
                if (id == requestId)
                {
                    var action = approved ? ApprovalDialogAction.Approve : ApprovalDialogAction.Deny;
                    _queue.Resolve(requestId, action);
                }
            };

            IdempotentApprovalEngine.RegisterResolver(resolver);

            try
            {
                var existingDecision = IdempotentApprovalEngine.GetDecision(requestId);
                ApprovalDialogAction action;

                if (existingDecision.HasValue)
                {
                    action = existingDecision.Value ? ApprovalDialogAction.Approve : ApprovalDialogAction.Deny;
                }
                else
                {
                    action = await _queue.EnqueueAsync(requestId);

                    bool approved = action == ApprovalDialogAction.Approve;
                    if (!IdempotentApprovalEngine.TryRegisterDecision(requestId, approved, null, out var error))
                    {
                        if (error != null)
                        {
                            Console.WriteLine($"[ERROR] Conflict detected in LumenApprovalHandler: {error}");
                            throw new InvalidOperationException(error);
                        }
                    }
                }

                // Log final approval result as a durable NoticeCell
                string statusStr = action == ApprovalDialogAction.Approve ? "APPROVED" : (action == ApprovalDialogAction.Deny ? "DENIED" : "CANCELLED");
                string level = action == ApprovalDialogAction.Approve ? "Success" : "Warning";
                _observer.UpdateState(new NoticeReceivedEvent($"[Approval] {tool} (File Edit) -> {statusStr}", level));

                return action == ApprovalDialogAction.Approve;
            }
            finally
            {
                IdempotentApprovalEngine.UnregisterResolver(resolver);
            }
        }
    }
}
