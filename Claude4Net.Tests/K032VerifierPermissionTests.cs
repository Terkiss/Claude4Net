using System;
using System.IO;
using System.Security;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K032 Verification Gate Hardening: 검증 세션 권한 및 보안 테스트
    /// 검증 세션의 읽기 전용 강제, 경로 탐색 방어, 세션 격리를 검증합니다.
    /// </summary>
    public class K032VerifierPermissionTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly VerificationOrchestrator _orchestrator;

        public K032VerifierPermissionTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "claude4net-k032-perm-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
            _orchestrator = new VerificationOrchestrator(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        /// <summary>
        /// 검증 세션은 읽기 전용이며, 쓰기 작업 시도 시 SecurityException 발생
        /// </summary>
        [Fact]
        public void Verifier_ReadOnlyCannotWriteWorkspace()
        {
            var session = _orchestrator.CreateVerifierSession();

            Assert.True(session.ReadOnlyMode);

            var ex = Assert.Throws<SecurityException>(() =>
                VerificationOrchestrator.EnforceReadOnly(session, "file_write"));

            Assert.Contains("읽기 전용", ex.Message);
            Assert.Contains("file_write", ex.Message);
        }

        /// <summary>
        /// PermissionEnforcer의 EvaluateForVerifier가 쓰기 도구를 차단하는지 검증
        /// </summary>
        [Fact]
        public void Verifier_PermissionEnforcerBlocksWriteTools()
        {
            var enforcer = new PermissionEnforcer();

            // 쓰기 도구 차단
            var writeResult = enforcer.EvaluateForVerifier("file_write", PathSafetyResult.Workspace, true);
            Assert.Equal(PermissionDecision.Deny, writeResult.Decision);
            Assert.Contains("write and execution tools are blocked", writeResult.Reason);

            // 편집 도구 차단
            var editResult = enforcer.EvaluateForVerifier("file_edit", PathSafetyResult.Workspace, true);
            Assert.Equal(PermissionDecision.Deny, editResult.Decision);

            // bash 도구 차단
            var bashResult = enforcer.EvaluateForVerifier("bash", PathSafetyResult.Workspace, true);
            Assert.Equal(PermissionDecision.Deny, bashResult.Decision);
        }

        /// <summary>
        /// PermissionEnforcer의 EvaluateForVerifier가 읽기 도구를 허용하는지 검증
        /// </summary>
        [Fact]
        public void Verifier_PermissionEnforcerAllowsReadTools()
        {
            var enforcer = new PermissionEnforcer();

            // 읽기 도구 허용 (isSensitiveTool = false)
            var readResult = enforcer.EvaluateForVerifier("file_read", PathSafetyResult.Workspace, false);
            Assert.Equal(PermissionDecision.Allow, readResult.Decision);
            Assert.Contains("read-only access allowed", readResult.Reason);
        }

        /// <summary>
        /// PermissionEnforcer의 EvaluateForVerifier가 워크스페이스 외부 접근을 차단하는지 검증
        /// </summary>
        [Fact]
        public void Verifier_PermissionEnforcerBlocksOutsideAccess()
        {
            var enforcer = new PermissionEnforcer();

            var outsideResult = enforcer.EvaluateForVerifier("file_read", PathSafetyResult.Outside, false);
            Assert.Equal(PermissionDecision.Deny, outsideResult.Decision);
            Assert.Contains("outside workspace access is blocked", outsideResult.Reason);
        }

        /// <summary>
        /// 검증 세션이 생성자 세션과 독립적인지 검증
        /// </summary>
        [Fact]
        public void Verifier_SessionIsIndependentFromGenerator()
        {
            var session = _orchestrator.CreateVerifierSession("gen-session-abc");

            Assert.NotEmpty(session.VerifierSessionId);
            Assert.Equal("gen-session-abc", session.GeneratorSessionId);
            Assert.True(session.ReadOnlyMode);
            Assert.StartsWith("verify-", session.VerifierSessionId);
        }

        /// <summary>
        /// 검증 세션 ID에 경로 탐색 문자가 있으면 저장 시 거부
        /// </summary>
        [Fact]
        public async Task Verifier_RejectsPathTraversalInSessionId()
        {
            var result = new VerificationResult
            {
                VerifierSessionId = "../../../etc/passwd",
                Verdict = VerificationVerdict.Pass
            };

            await Assert.ThrowsAsync<ArgumentException>(() =>
                _orchestrator.WriteResultAsync(result));
        }

        /// <summary>
        /// 검증 결과 로드 시 존재하지 않는 세션에 대해 null 반환
        /// </summary>
        [Fact]
        public async Task Verifier_LoadNonExistentSessionReturnsNull()
        {
            var result = await _orchestrator.LoadResultAsync("nonexistent-session-id");
            Assert.Null(result);
        }

        /// <summary>
        /// VerificationCheck의 기본 Result가 Fail인지 검증 (default-fail 모델 수준 강제)
        /// </summary>
        [Fact]
        public void Verifier_DefaultCheckResultIsFail()
        {
            var check = new VerificationCheck
            {
                Name = "Test Check",
                Command = "echo test"
            };

            Assert.Equal(VerificationVerdict.Fail, check.Result);
        }
    }
}
