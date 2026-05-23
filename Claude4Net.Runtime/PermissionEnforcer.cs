using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    public enum PermissionDecision
    {
        Allow,
        RequireApproval,
        Deny
    }

    public sealed record PermissionEnforcementResult(PermissionDecision Decision, string Reason);

    public sealed class PermissionEnforcer
    {
        public PermissionEnforcementResult Evaluate(
            PermissionMode mode,
            string toolName,
            PathSafetyResult pathSafety,
            bool isSensitiveTool,
            CommandRiskAssessment commandRisk)
        {
            var normalizedMode = Normalize(mode);

            if (normalizedMode == PermissionMode.DangerFullAccess)
            {
                if (pathSafety == PathSafetyResult.Outside)
                {
                    return new PermissionEnforcementResult(PermissionDecision.RequireApproval, "outside workspace access requires explicit approval");
                }
                else
                {
                    return new PermissionEnforcementResult(PermissionDecision.Allow, "allowed in DangerFullAccess mode inside workspace");
                }
            }

            if (pathSafety == PathSafetyResult.Outside)
            {
                return new PermissionEnforcementResult(PermissionDecision.Deny, "outside workspace access is blocked");
            }

            if (normalizedMode == PermissionMode.ReadOnly && IsWriteOrExecutionTool(toolName, isSensitiveTool))
            {
                return new PermissionEnforcementResult(PermissionDecision.Deny, "read-only mode blocks writes and shell execution");
            }

            if (commandRisk.Level == CommandRiskLevel.Dangerous)
            {
                return normalizedMode == PermissionMode.DangerFullAccess
                    ? new PermissionEnforcementResult(PermissionDecision.RequireApproval, commandRisk.Reason)
                    : new PermissionEnforcementResult(PermissionDecision.Deny, commandRisk.Reason);
            }

            if (commandRisk.Level == CommandRiskLevel.NeedsApproval)
            {
                return normalizedMode == PermissionMode.WorkspaceWrite
                    ? new PermissionEnforcementResult(PermissionDecision.Allow, commandRisk.Reason)
                    : new PermissionEnforcementResult(PermissionDecision.RequireApproval, commandRisk.Reason);
            }

            if (normalizedMode == PermissionMode.Prompt && isSensitiveTool)
            {
                return new PermissionEnforcementResult(PermissionDecision.RequireApproval, "sensitive tool requires approval in prompt mode");
            }

            return new PermissionEnforcementResult(PermissionDecision.Allow, "allowed by permission policy");
        }

        public static PermissionMode Normalize(PermissionMode mode) => mode switch
        {
            PermissionMode.Default => PermissionMode.Prompt,
            PermissionMode.Yolo => PermissionMode.DangerFullAccess,
            PermissionMode.BypassPermissions => PermissionMode.DangerFullAccess,
            _ => mode
        };

        /// <summary>
        /// 검증 세션 전용 권한 평가입니다.
        /// 검증 세션은 워크스페이스에 쓸 수 없으며, 읽기 및 비파괴적 명령만 허용됩니다.
        /// </summary>
        public PermissionEnforcementResult EvaluateForVerifier(
            string toolName,
            PathSafetyResult pathSafety,
            bool isSensitiveTool)
        {
            // 검증 세션은 항상 ReadOnly로 강제됩니다.
            if (pathSafety == PathSafetyResult.Outside)
            {
                return new PermissionEnforcementResult(PermissionDecision.Deny,
                    "verifier session: outside workspace access is blocked");
            }

            if (IsWriteOrExecutionTool(toolName, isSensitiveTool))
            {
                return new PermissionEnforcementResult(PermissionDecision.Deny,
                    "verifier session: write and execution tools are blocked in read-only verification mode");
            }

            return new PermissionEnforcementResult(PermissionDecision.Allow,
                "verifier session: read-only access allowed");
        }

        private static bool IsWriteOrExecutionTool(string toolName, bool isSensitiveTool)
        {
            if (!isSensitiveTool) return false;

            return toolName.Contains("write", System.StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("edit", System.StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("delete", System.StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("bash", System.StringComparison.OrdinalIgnoreCase) ||
                   toolName.Contains("shell", System.StringComparison.OrdinalIgnoreCase) ||
                   toolName.Equals("sh", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
