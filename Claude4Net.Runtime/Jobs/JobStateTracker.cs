using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Claude4Net.Runtime.Jobs
{
    public class JobStateTracker
    {
        private static readonly ConcurrentDictionary<string, JobState> _states = new ConcurrentDictionary<string, JobState>();
        private static readonly ConcurrentDictionary<string, CommandResult> _commands = new ConcurrentDictionary<string, CommandResult>();

        public static JobState GetOrCreateState(string jobId)
        {
            return _states.GetOrAdd(jobId, id => new JobState { JobId = id });
        }

        public static bool TryGetState(string jobId, out JobState state)
        {
            return _states.TryGetValue(jobId, out state!);
        }

        public static void UpdateState(string jobId, Action<JobState> updateAction)
        {
            var state = GetOrCreateState(jobId);
            lock (state)
            {
                updateAction(state);
                state.Sequence++;
            }
        }

        public static CommandResult ProcessCommand(string jobId, string commandId, string commandType, Action executeAction)
        {
            return _commands.GetOrAdd(commandId, cid =>
            {
                try
                {
                    executeAction();
                    return new CommandResult { CommandId = cid, Success = true, Message = $"Command {commandType} executed successfully." };
                }
                catch (Exception ex)
                {
                    return new CommandResult { CommandId = cid, Success = false, Message = ex.Message };
                }
            });
        }
        
        public static void Clear()
        {
            _states.Clear();
            _commands.Clear();
        }
    }

    public class JobState
    {
        public string JobId { get; set; } = string.Empty;
        public int Sequence { get; set; } = 1;
        public double Progress { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string LatestMessage { get; set; } = string.Empty;
        public bool PendingApproval { get; set; }
        public List<string> ChangedFiles { get; set; } = new List<string>();
        public string VerificationState { get; set; } = string.Empty;
    }

    public class CommandResult
    {
        public string CommandId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
