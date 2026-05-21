using System;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public class RoutineRunner
    {
        private readonly RoutineStore _store;
        private readonly PermissionEnforcer _permissionEnforcer;
        private readonly PathSafetyEvaluator _pathSafety;

        public RoutineRunner(RoutineStore store, PermissionEnforcer permissionEnforcer, PathSafetyEvaluator pathSafety)
        {
            _store = store;
            _permissionEnforcer = permissionEnforcer;
            _pathSafety = pathSafety;
        }

        public async Task<RoutineRunRecord> RunAsync(string routineId, string workspaceRoot, PermissionMode currentSessionMode)
        {
            var record = new RoutineRunRecord
            {
                RunId = Guid.NewGuid().ToString("N"),
                RoutineId = routineId,
                StartedAt = DateTimeOffset.UtcNow
            };

            try
            {
                var routine = await _store.LoadAsync(routineId);
                if (routine == null) throw new InvalidOperationException($"Routine {routineId} not found.");
                if (!routine.Enabled) throw new InvalidOperationException($"Routine {routineId} is disabled.");

                if (PermissionEnforcer.Normalize(currentSessionMode) == PermissionMode.ReadOnly &&
                    PermissionEnforcer.Normalize(routine.RequiredPermissionMode) != PermissionMode.ReadOnly)
                {
                    throw new UnauthorizedAccessException("Current session is ReadOnly, but routine requires higher permissions.");
                }

                foreach (var action in routine.Actions)
                {
                    if (action.Kind == RoutineActionKind.Script)
                    {
                        var pathResult = _pathSafety.EvaluateSinglePathSafety(action.Payload);
                        var eval = _permissionEnforcer.Evaluate(currentSessionMode, "bash", pathResult, true, new CommandRiskAssessment(CommandRiskLevel.Dangerous, "script execution", Array.Empty<string>()));
                        if (eval.Decision == PermissionDecision.Deny)
                        {
                            throw new UnauthorizedAccessException($"Script execution denied: {eval.Reason}");
                        }
                    }
                }

                record.Success = true;
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.Error = ex.Message;
            }
            finally
            {
                record.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveRunRecordAsync(record);
            }

            return record;
        }
    }
}
