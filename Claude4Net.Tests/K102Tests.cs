using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Claude4Net.Runtime.Jobs;
using Claude4Net.Dashboard.Controllers;

namespace Claude4Net.Tests
{
    [Trait("Category", "K102")]
    public class K102Tests : IDisposable
    {
        public K102Tests()
        {
            JobStateTracker.Clear();
        }

        public void Dispose()
        {
            JobStateTracker.Clear();
        }

        [Fact]
        public void DeltaFraming_SequenceValidation()
        {
            var jobId = "test-job-102";
            var controller = new JobController();

            // 1. Initial request when job is not tracked -> should return NotFound
            var getResultNotFound = controller.GetFrame(jobId, 0);
            Assert.IsType<NotFoundObjectResult>(getResultNotFound);

            // 2. Register job state
            JobStateTracker.GetOrCreateState(jobId);

            // 3. Request with afterSeq = 0. Since sequence starts at 1, 1 > 0, so should return 200 OK.
            var getResultOk = controller.GetFrame(jobId, 0);
            var okResult = Assert.IsType<OkObjectResult>(getResultOk);
            var value = okResult.Value;
            var sequence = (int)value.GetType().GetProperty("sequence").GetValue(value);
            var resultJobId = (string)value.GetType().GetProperty("jobId").GetValue(value);
            Assert.Equal(1, sequence);
            Assert.Equal(jobId, resultJobId);


            // 4. Request with afterSeq = 1. Since sequence is 1, 1 <= 1, so should return 204 No Content.
            var getResultNoContent = controller.GetFrame(jobId, 1);
            Assert.IsType<NoContentResult>(getResultNoContent);

            // 5. Update state -> sequence increments to 2
            JobStateTracker.UpdateState(jobId, state =>
            {
                state.Progress = 50.0;
                state.Phase = "Compiling";
                state.LatestMessage = "Compiling source files...";
            });

            // 6. Request with afterSeq = 1 -> should return 200 OK because 2 > 1.
            var getResultOk2 = controller.GetFrame(jobId, 1);
            var okResult2 = Assert.IsType<OkObjectResult>(getResultOk2);
            var value2 = okResult2.Value;
            var sequence2 = (int)value2.GetType().GetProperty("sequence").GetValue(value2);
            var progress2 = (double)value2.GetType().GetProperty("progress").GetValue(value2);
            var phase2 = (string)value2.GetType().GetProperty("phase").GetValue(value2);
            Assert.Equal(2, sequence2);
            Assert.Equal(50.0, progress2);
            Assert.Equal("Compiling", phase2);


            // 7. Request with afterSeq = 2 -> should return 204 No Content because 2 <= 2.
            var getResultNoContent2 = controller.GetFrame(jobId, 2);
            Assert.IsType<NoContentResult>(getResultNoContent2);
        }

        [Fact]
        public void CommandIdempotency_WithSameCommandId()
        {
            var jobId = "test-job-cmd-102";
            var controller = new JobController();
            JobStateTracker.GetOrCreateState(jobId);

            var commandId = "cmd-unique-123";
            var request = new JobController.CommandRequest
            {
                CommandId = commandId,
                CommandType = "CancelJob"
            };

            // 1. Send first command -> Success, state phase changes to "Cancelled", sequence increases to 2
            var result1 = controller.PostCommand(jobId, request);
            var okResult1 = Assert.IsType<OkObjectResult>(result1);
            var val1 = okResult1.Value;
            var success1 = (bool)val1.GetType().GetProperty("success").GetValue(val1);
            var cmdId1 = (string)val1.GetType().GetProperty("commandId").GetValue(val1);
            Assert.True(success1);
            Assert.Equal(commandId, cmdId1);

            // Verify state changes
            JobStateTracker.TryGetState(jobId, out var stateAfterFirst);
            Assert.Equal("Cancelled", stateAfterFirst.Phase);
            Assert.Equal(2, stateAfterFirst.Sequence);

            // 2. Send second command with the same commandId -> Idempotent, returns success, but action is not run again
            // So state sequence does not increase and remains 2.
            var result2 = controller.PostCommand(jobId, request);
            var okResult2 = Assert.IsType<OkObjectResult>(result2);
            var val2 = okResult2.Value;
            var success2 = (bool)val2.GetType().GetProperty("success").GetValue(val2);
            var cmdId2 = (string)val2.GetType().GetProperty("commandId").GetValue(val2);
            Assert.True(success2);
            Assert.Equal(commandId, cmdId2);

            JobStateTracker.TryGetState(jobId, out var stateAfterSecond);
            Assert.Equal("Cancelled", stateAfterSecond.Phase);
            Assert.Equal(2, stateAfterSecond.Sequence); // Remains 2!

        }
    }
}
