using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
        public static System.Threading.AsyncLocal<string?> ActiveCommand { get; } = new();
        public static System.Threading.AsyncLocal<string?> ActivePath { get; } = new();

        private static SecurityPolicyConfig _config = SecurityPolicyConfig.CreateDefault();
        
        public static SecurityPolicyConfig Config
        {
            get => _config;
            set => _config = value;
        }

        public static string ActiveProfileName
        {
            get => _config.ActiveProfile;
            set => _config.ActiveProfile = value;
        }

        public static SecurityProfile ActiveProfile
        {
            get
            {
                if (_config.Profiles.TryGetValue(ActiveProfileName, out var profile))
                {
                    return profile;
                }
                return _config.Profiles.Values.FirstOrDefault() ?? new SecurityProfile();
            }
        }

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

            var profile = ActiveProfile;

            // 1. Path Safety & Traversal Checks
            string? activePath = ActivePath.Value;
            if (!string.IsNullOrEmpty(activePath))
            {
                if (!IsPathAllowed(activePath, profile))
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, $"Path access blocked by security policy profile ({ActiveProfileName}): {activePath}");
                }
            }

            if (pathSafety == PathSafetyResult.Outside && !profile.AllowOutsideWorkspace)
            {
                // Verify if we have an active path, and if it is not allowed. Or if no active path is set but pathSafety is Outside, block it.
                if (string.IsNullOrEmpty(activePath) || !IsPathAllowed(activePath, profile))
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, "outside workspace access is blocked");
                }
            }

            // 2. Command Checks
            string? activeCommand = ActiveCommand.Value;
            if (!string.IsNullOrEmpty(activeCommand))
            {
                if (!IsCommandAllowed(activeCommand, profile))
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, $"Command blocked by security policy profile ({ActiveProfileName}): {activeCommand}");
                }
            }

            if (pathSafety == PathSafetyResult.Outside)
            {
                if (!profile.AllowOutsideWorkspace)
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, "outside workspace access is blocked");
                }
                return new PermissionEnforcementResult(PermissionDecision.RequireApproval, "outside workspace access requires explicit approval");
            }

            if (normalizedMode == PermissionMode.ReadOnly && IsWriteOrExecutionTool(toolName, isSensitiveTool))
            {
                return new PermissionEnforcementResult(PermissionDecision.Deny, "read-only mode blocks writes and shell execution");
            }

            if (commandRisk.Level == CommandRiskLevel.Dangerous)
            {
                if (profile.Level == SecurityProfileLevel.Strict)
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, $"Dangerous command blocked by strict security profile: {commandRisk.Reason}");
                }

                return normalizedMode == PermissionMode.DangerFullAccess
                    ? new PermissionEnforcementResult(PermissionDecision.RequireApproval, commandRisk.Reason)
                    : new PermissionEnforcementResult(PermissionDecision.Deny, commandRisk.Reason);
            }

            if (commandRisk.Level == CommandRiskLevel.NeedsApproval)
            {
                if (profile.Level == SecurityProfileLevel.Strict)
                {
                    return new PermissionEnforcementResult(PermissionDecision.Deny, $"Command requiring approval blocked by strict security profile: {commandRisk.Reason}");
                }

                return normalizedMode == PermissionMode.WorkspaceWrite
                    ? new PermissionEnforcementResult(PermissionDecision.Allow, commandRisk.Reason)
                    : new PermissionEnforcementResult(PermissionDecision.RequireApproval, commandRisk.Reason);
            }

            if (profile.RequireApprovalForSensitiveTools && isSensitiveTool)
            {
                return new PermissionEnforcementResult(PermissionDecision.RequireApproval, $"sensitive tool '{toolName}' requires approval under profile '{ActiveProfileName}'");
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

        public static bool IsCommandAllowed(string command, SecurityProfile profile)
        {
            if (string.IsNullOrWhiteSpace(command)) return true;

            // First check allowed patterns (whitelist) if not empty
            bool isAllowedByWhitelist = false;
            bool hasWhitelist = profile.AllowedCommandPatterns.Count > 0;
            if (hasWhitelist)
            {
                foreach (var pattern in profile.AllowedCommandPatterns)
                {
                    if (string.IsNullOrEmpty(pattern)) continue;
                    try
                    {
                        if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                        {
                            isAllowedByWhitelist = true;
                            break;
                        }
                    }
                    catch { }
                }
                if (!isAllowedByWhitelist) return false;
            }

            // Next check blocked patterns (blacklist)
            foreach (var pattern in profile.BlockedCommandPatterns)
            {
                if (string.IsNullOrEmpty(pattern)) continue;
                if (pattern == ".*" && isAllowedByWhitelist)
                {
                    // Skip catch-all block if explicitly allowed by whitelist
                    continue;
                }
                try
                {
                    if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
                    {
                        return false;
                    }
                }
                catch { }
            }

            return true;
        }

        public static bool IsPathAllowed(string path, SecurityProfile profile)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;

            // Directory traversal check
            if (profile.BlockDirectoryTraversal)
            {
                if (path.Contains(".."))
                {
                    // directory traversal attempted
                    return false;
                }
            }

            // Check blocked folders (blacklist)
            foreach (var blocked in profile.BlockedFolders)
            {
                if (IsPathInsideFolder(path, blocked))
                {
                    return false;
                }
            }

            // Check if it is inside workspace
            string? cwd = AppState.CurrentCwd;
            bool isInsideWorkspace = false;
            if (!string.IsNullOrEmpty(cwd))
            {
                isInsideWorkspace = IsPathInsideFolder(path, cwd);
            }

            if (isInsideWorkspace)
            {
                return true;
            }

            // Path is outside workspace. Check if we allow outside workspace.
            if (!profile.AllowOutsideWorkspace)
            {
                // But wait! Is it in allowed folders?
                bool inAllowedFolder = false;
                foreach (var allowed in profile.AllowedFolders)
                {
                    if (IsPathInsideFolder(path, allowed))
                    {
                        inAllowedFolder = true;
                        break;
                    }
                }
                if (!inAllowedFolder)
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsPathInsideFolder(string path, string folder)
        {
            try
            {
                if (folder == "*") return true;

                string fullPath = Path.GetFullPath(path);
                string fullFolder = Path.GetFullPath(folder);

                string p = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string f = fullFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class SecurityPolicyHook : IToolHook
    {
        public string Name => "SecurityPolicyHook";
        public HookTiming Timing => HookTiming.BeforeToolExecution;
        public int Priority => -100; // Run early
        public bool IsEnabled { get; set; } = true;

        public Task<HookResult> ExecuteAsync(HookContext context)
        {
            try
            {
                if (!string.IsNullOrEmpty(context.Arguments))
                {
                    using var doc = JsonDocument.Parse(context.Arguments);
                    var root = doc.RootElement;

                    string? command = null;
                    if (root.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.String)
                    {
                        command = cmdProp.GetString();
                    }

                    string? path = null;
                    if (root.TryGetProperty("path", out var pathProp) && pathProp.ValueKind == JsonValueKind.String)
                    {
                        path = pathProp.GetString();
                    }
                    else if (root.TryGetProperty("file_path", out var filePathProp) && filePathProp.ValueKind == JsonValueKind.String)
                    {
                        path = filePathProp.GetString();
                    }
                    else if (root.TryGetProperty("target", out var targetProp) && targetProp.ValueKind == JsonValueKind.String)
                    {
                        path = targetProp.GetString();
                    }
                    else if (root.TryGetProperty("file", out var fileProp) && fileProp.ValueKind == JsonValueKind.String)
                    {
                        path = fileProp.GetString();
                    }

                    PermissionEnforcer.ActiveCommand.Value = command;
                    PermissionEnforcer.ActivePath.Value = path;
                }
            }
            catch
            {
                // Best effort
            }
            return Task.FromResult(HookResult.Ok(Name));
        }
    }
}
