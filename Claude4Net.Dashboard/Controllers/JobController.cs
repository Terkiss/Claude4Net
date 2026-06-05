using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Claude4Net.Runtime.Jobs;

namespace Claude4Net.Dashboard.Controllers
{
    [ApiController]
    [Route("api/jobs")]
    public class JobController : ControllerBase
    {
        public class CommandRequest
        {
            public string CommandId { get; set; } = string.Empty;
            public string CommandType { get; set; } = string.Empty;
            public string? Payload { get; set; }
        }

        [HttpGet("{jobId}/frame")]
        public async Task<IActionResult> GetFrame([FromRoute] string jobId, [FromQuery] int afterSeq = 0, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest("Job ID is required.");
            }

            if (!JobStateTracker.TryGetState(jobId, out var state))
            {
                return NotFound($"Job {jobId} not found.");
            }

            int timeoutMs = 30000;
            int intervalMs = 66; // ~15fps
            int elapsed = 0;

            while (elapsed < timeoutMs)
            {
                if (cancellationToken.IsCancellationRequested) return NoContent();

                int currentSeq;
                lock (state)
                {
                    currentSeq = state.Sequence;
                }

                if (currentSeq > afterSeq)
                {
                    lock (state)
                    {
                        return Ok(new
                        {
                            jobId = state.JobId,
                            sequence = state.Sequence,
                            progress = state.Progress,
                            phase = state.Phase,
                            latestMessage = state.LatestMessage,
                            pendingApproval = state.PendingApproval,
                            changedFiles = state.ChangedFiles,
                            verificationState = state.VerificationState
                        });
                    }
                }

                try
                {
                    await Task.Delay(intervalMs, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return NoContent();
                }
                elapsed += intervalMs;
            }

            return NoContent(); // 204 No Content if sequence matches/has no changes after timeout
        }

        [HttpPost("{jobId}/commands")]
        public IActionResult PostCommand([FromRoute] string jobId, [FromBody] CommandRequest request)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return BadRequest("Job ID is required.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.CommandId) || string.IsNullOrWhiteSpace(request.CommandType))
            {
                return BadRequest("Invalid command parameters.");
            }

            if (!JobStateTracker.TryGetState(jobId, out var state))
            {
                return NotFound($"Job {jobId} not found.");
            }

            var result = JobStateTracker.ProcessCommand(jobId, request.CommandId, request.CommandType, () =>
            {
                JobStateTracker.UpdateState(jobId, s =>
                {
                    switch (request.CommandType.ToLowerInvariant())
                    {
                        case "approvetool":
                        case "approve_tool":
                        case "approve-tool":
                            s.PendingApproval = false;
                            s.LatestMessage = "Tools approved.";
                            break;

                        case "canceljob":
                        case "cancel_job":
                        case "cancel-job":
                            s.Phase = "Cancelled";
                            s.LatestMessage = "Job cancelled.";
                            break;

                        case "approvegitpush":
                        case "approve_git_push":
                        case "approve-git-push":
                            s.LatestMessage = "Git push approved.";
                            break;

                        default:
                            throw new ArgumentException($"Unknown command type: {request.CommandType}");
                    }
                });
            });

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(new
            {
                commandId = result.CommandId,
                success = result.Success,
                message = result.Message
            });
        }
    }
}
