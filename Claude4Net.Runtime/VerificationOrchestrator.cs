using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 독립적인 검증 세션을 관리하고 default-fail 정책 기반의 검증 체크를 실행하는 오케스트레이터입니다.
    /// 검증 세션은 생성자 컨텍스트를 상속하지 않으며, 읽기 전용으로 실행됩니다.
    /// </summary>
    public class VerificationOrchestrator
    {
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly string _workspaceRoot;
        private readonly string _sessionsBaseDir;

        public VerificationOrchestrator(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(workspaceRoot))
                throw new ArgumentException("Workspace root cannot be null or empty.", nameof(workspaceRoot));

            _workspaceRoot = Path.GetFullPath(workspaceRoot);
            _sessionsBaseDir = Path.Combine(_workspaceRoot, ".claude4net", "sessions");
        }

        /// <summary>
        /// 생성자 컨텍스트를 상속하지 않는 독립 검증 세션을 생성합니다.
        /// 검증 세션은 항상 읽기 전용으로 실행됩니다.
        /// </summary>
        public VerifierSessionRecord CreateVerifierSession(string? generatorSessionId = null)
        {
            string verifierSessionId = $"verify-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40];

            var record = new VerifierSessionRecord
            {
                VerifierSessionId = verifierSessionId,
                GeneratorSessionId = generatorSessionId,
                ReadOnlyMode = true,
                CreatedAt = DateTimeOffset.UtcNow,
                WorkspacePath = _workspaceRoot
            };

            return record;
        }

        /// <summary>
        /// 개별 검증 체크를 실행합니다.
        /// Default-fail 정책: 명령 출력이 없거나 증거가 없으면 Fail.
        /// </summary>
        /// <param name="checkName">체크 이름</param>
        /// <param name="command">실행할 명령</param>
        /// <param name="commandOutput">명령 실행 출력 (null이면 Fail)</param>
        /// <param name="exitCode">명령 종료 코드 (null이면 실행되지 않은 것으로 간주)</param>
        /// <param name="evidenceFilePath">증거 파일 경로 (선택)</param>
        /// <returns>판정된 VerificationCheck</returns>
        public VerificationCheck RunCheck(
            string checkName,
            string command,
            string? commandOutput,
            int? exitCode,
            string? evidenceFilePath = null)
        {
            var startedAt = DateTimeOffset.UtcNow;

            // Default-fail 정책: 모든 체크는 Fail로 시작
            if (commandOutput == null || exitCode == null)
            {
                return new VerificationCheck
                {
                    Name = checkName,
                    Command = command,
                    Result = VerificationVerdict.Fail,
                    Evidence = null,
                    Notes = "명령이 실행되지 않았거나 출력이 캡처되지 않음 (default-fail)",
                    Skipped = false,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // 명령이 실행되었으나 실패한 경우
            if (exitCode != 0)
            {
                return new VerificationCheck
                {
                    Name = checkName,
                    Command = command,
                    OutputFile = evidenceFilePath,
                    Result = VerificationVerdict.Fail,
                    Evidence = TruncateEvidence(commandOutput),
                    Notes = $"명령이 exit code {exitCode}로 실패",
                    Skipped = false,
                    StartedAt = startedAt,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // 명령이 성공한 경우 - 증거 파일이 있으면 확인
            bool evidenceVerified = true;
            if (evidenceFilePath != null)
            {
                string fullEvidencePath = Path.Combine(_workspaceRoot, evidenceFilePath);
                evidenceVerified = File.Exists(fullEvidencePath);
            }

            return new VerificationCheck
            {
                Name = checkName,
                Command = command,
                OutputFile = evidenceFilePath,
                Result = evidenceVerified ? VerificationVerdict.Pass : VerificationVerdict.Partial,
                Evidence = TruncateEvidence(commandOutput),
                Notes = evidenceVerified ? "명령 성공 및 증거 확인됨" : "명령 성공이나 증거 파일이 누락됨",
                Skipped = false,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// 명시적으로 건너뛴 체크를 기록합니다.
        /// 건너뛴 체크는 Partial로 처리되며 이유가 기록됩니다.
        /// </summary>
        public VerificationCheck SkipCheck(string checkName, string command, string reason)
        {
            return new VerificationCheck
            {
                Name = checkName,
                Command = command,
                Result = VerificationVerdict.Partial,
                Notes = $"건너뜀: {reason}",
                Skipped = true,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// 모든 체크 결과를 집계하여 최종 검증 결과를 생성합니다.
        /// Verdict 결정 규칙:
        /// - 모든 체크 Pass → Pass
        /// - 하나라도 Fail → Fail
        /// - Fail 없으나 Partial 존재 → Partial
        /// </summary>
        public VerificationResult AggregateResult(
            string verifierSessionId,
            string? generatorSessionId,
            IReadOnlyList<VerificationCheck> checks)
        {
            var verdict = DetermineVerdict(checks);

            return new VerificationResult
            {
                VerifierSessionId = verifierSessionId,
                GeneratorSessionId = generatorSessionId,
                Verdict = verdict,
                Checks = checks,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// 검증 결과를 JSON 파일로 저장합니다.
        /// 저장 경로: .claude4net/sessions/{verifierSessionId}/verification-result.json
        /// </summary>
        public async Task WriteResultAsync(VerificationResult result)
        {
            ValidateSessionId(result.VerifierSessionId);

            string sessionDir = Path.Combine(_sessionsBaseDir, result.VerifierSessionId);
            string fullSessionDir = Path.GetFullPath(sessionDir);
            string fullBaseDir = Path.GetFullPath(_sessionsBaseDir);

            // Path traversal 방어
            if (!fullSessionDir.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Session directory escape detected.");

            Directory.CreateDirectory(fullSessionDir);

            string resultPath = Path.Combine(fullSessionDir, "verification-result.json");
            string json = JsonSerializer.Serialize(result, _jsonOptions);
            await File.WriteAllTextAsync(resultPath, json);
        }

        /// <summary>
        /// 저장된 검증 결과를 로드합니다.
        /// </summary>
        public async Task<VerificationResult?> LoadResultAsync(string verifierSessionId)
        {
            ValidateSessionId(verifierSessionId);

            string sessionDir = Path.Combine(_sessionsBaseDir, verifierSessionId);
            string fullSessionDir = Path.GetFullPath(sessionDir);
            string fullBaseDir = Path.GetFullPath(_sessionsBaseDir);

            if (!fullSessionDir.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Session directory escape detected.");

            string resultPath = Path.Combine(fullSessionDir, "verification-result.json");
            if (!File.Exists(resultPath)) return null;

            string json = await File.ReadAllTextAsync(resultPath);
            return JsonSerializer.Deserialize<VerificationResult>(json);
        }

        /// <summary>
        /// 검증 세션이 워크스페이스에 쓸 수 있는지 확인합니다.
        /// 검증 세션은 항상 읽기 전용이므로 쓰기를 시도하면 SecurityException을 발생시킵니다.
        /// </summary>
        public static void EnforceReadOnly(VerifierSessionRecord session, string operation)
        {
            if (session.ReadOnlyMode)
            {
                throw new SecurityException(
                    $"검증 세션은 읽기 전용입니다. 쓰기 작업 '{operation}'이 차단되었습니다.");
            }
        }

        /// <summary>
        /// 검증 결과를 CLI에 출력할 수 있는 형식으로 포맷합니다.
        /// </summary>
        public static string FormatResultForCli(VerificationResult result)
        {
            var lines = new List<string>
            {
                $"VERDICT: {result.Verdict}",
                $"Session: {result.VerifierSessionId}",
                $"Generator: {result.GeneratorSessionId ?? "N/A"}",
                $"Timestamp: {result.Timestamp:yyyy-MM-dd HH:mm:ss UTC}",
                "",
                "Checks:"
            };

            foreach (var check in result.Checks)
            {
                string statusIcon = check.Result switch
                {
                    VerificationVerdict.Pass => "✅",
                    VerificationVerdict.Fail => "❌",
                    VerificationVerdict.Partial => "⚠️",
                    _ => "?"
                };

                string skippedTag = check.Skipped ? " [SKIPPED]" : "";
                lines.Add($"  {statusIcon} {check.Name}{skippedTag}: {check.Result}");

                if (!string.IsNullOrEmpty(check.Notes))
                    lines.Add($"     Notes: {check.Notes}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static VerificationVerdict DetermineVerdict(IReadOnlyList<VerificationCheck> checks)
        {
            if (checks.Count == 0) return VerificationVerdict.Fail;

            bool anyFail = checks.Any(c => c.Result == VerificationVerdict.Fail);
            bool anyPartial = checks.Any(c => c.Result == VerificationVerdict.Partial);

            if (anyFail) return VerificationVerdict.Fail;
            if (anyPartial) return VerificationVerdict.Partial;
            return VerificationVerdict.Pass;
        }

        private static string TruncateEvidence(string output, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(output)) return string.Empty;
            return output.Length <= maxLength ? output : output[..maxLength] + "... [truncated]";
        }

        private static void ValidateSessionId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));

            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
                throw new ArgumentException("Invalid characters in session ID.", nameof(sessionId));

            if (Path.IsPathRooted(sessionId))
                throw new ArgumentException("Session ID cannot be a rooted path.", nameof(sessionId));
        }
    }
}
