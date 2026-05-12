using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests
{
    /// <summary>
    /// K032 Verification Gate Hardening: 핵심 검증 로직 테스트
    /// Default-fail 정책, 증거 기반 판정, 결과 파싱/감사 가능성을 검증합니다.
    /// </summary>
    public class K032VerificationGateTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly VerificationOrchestrator _orchestrator;

        public K032VerificationGateTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "claude4net-k032-test-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
            _orchestrator = new VerificationOrchestrator(_tempDir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        /// <summary>
        /// Default-fail: 증거(명령 출력)가 없으면 기본적으로 Fail
        /// </summary>
        [Fact]
        public void Verifier_DefaultFailWithoutEvidence()
        {
            // 명령 출력이 null인 경우 → Fail
            var check = _orchestrator.RunCheck("Build", "dotnet build", null, null);
            Assert.Equal(VerificationVerdict.Fail, check.Result);
            Assert.Contains("실행되지 않았거나", check.Notes);
        }

        /// <summary>
        /// 명령 출력이 있고 exit code가 0이면 Pass
        /// </summary>
        [Fact]
        public void Verifier_RequiresCommandOutputForPass()
        {
            var check = _orchestrator.RunCheck("Build", "dotnet build", "Build succeeded.", 0);
            Assert.Equal(VerificationVerdict.Pass, check.Result);
            Assert.Contains("Build succeeded", check.Evidence);
        }

        /// <summary>
        /// 명령이 실패(exit code != 0)하면 Fail
        /// </summary>
        [Fact]
        public void Verifier_FailOnNonZeroExitCode()
        {
            var check = _orchestrator.RunCheck("Build", "dotnet build", "Error CS1234", 1);
            Assert.Equal(VerificationVerdict.Fail, check.Result);
            Assert.Contains("exit code 1", check.Notes);
        }

        /// <summary>
        /// Pass/Fail/Partial 판정이 올바르게 집계되는지 검증
        /// </summary>
        [Fact]
        public void Verifier_ParsesPassFailPartial()
        {
            var checks = new List<VerificationCheck>
            {
                _orchestrator.RunCheck("Build", "dotnet build", "Success", 0),
                _orchestrator.RunCheck("Test", "dotnet test", "All passed", 0)
            };

            var result = _orchestrator.AggregateResult("verify-test", null, checks);
            Assert.Equal(VerificationVerdict.Pass, result.Verdict);

            // Fail이 하나라도 있으면 전체 Fail
            var checksWithFail = new List<VerificationCheck>
            {
                _orchestrator.RunCheck("Build", "dotnet build", "Success", 0),
                _orchestrator.RunCheck("Test", "dotnet test", null, null) // Fail
            };

            var resultWithFail = _orchestrator.AggregateResult("verify-test-2", null, checksWithFail);
            Assert.Equal(VerificationVerdict.Fail, resultWithFail.Verdict);
        }

        /// <summary>
        /// 건너뛴 체크가 명시적으로 기록되는지 검증
        /// </summary>
        [Fact]
        public void Verifier_SkippedChecksExplicitlyRecorded()
        {
            var skipped = _orchestrator.SkipCheck("Linux Build", "bash build.sh", "Windows 환경에서 실행 불가");
            Assert.True(skipped.Skipped);
            Assert.Equal(VerificationVerdict.Partial, skipped.Result);
            Assert.Contains("건너뜀", skipped.Notes);
        }

        /// <summary>
        /// 검증 결과가 JSON으로 저장되고 다시 로드할 수 있는지 검증
        /// </summary>
        [Fact]
        public async Task VerifyCommand_WritesMachineReadableResult()
        {
            var session = _orchestrator.CreateVerifierSession("gen-session-123");
            var checks = new List<VerificationCheck>
            {
                _orchestrator.RunCheck("Build", "dotnet build", "Build succeeded.", 0),
                _orchestrator.RunCheck("Test", "dotnet test", "12 tests passed", 0)
            };

            var result = _orchestrator.AggregateResult(session.VerifierSessionId, session.GeneratorSessionId, checks);
            await _orchestrator.WriteResultAsync(result);

            // 로드하여 확인
            var loaded = await _orchestrator.LoadResultAsync(session.VerifierSessionId);
            Assert.NotNull(loaded);
            Assert.Equal(VerificationVerdict.Pass, loaded.Verdict);
            Assert.Equal(2, loaded.Checks.Count);
            Assert.Equal("gen-session-123", loaded.GeneratorSessionId);
        }

        /// <summary>
        /// 체크가 0개이면 Fail로 판정
        /// </summary>
        [Fact]
        public void Verifier_EmptyChecksResultInFail()
        {
            var result = _orchestrator.AggregateResult("verify-empty", null, new List<VerificationCheck>());
            Assert.Equal(VerificationVerdict.Fail, result.Verdict);
        }

        /// <summary>
        /// CLI 포맷 출력이 VERDICT를 포함하는지 검증
        /// </summary>
        [Fact]
        public void Verifier_FormatContainsVerdict()
        {
            var checks = new List<VerificationCheck>
            {
                _orchestrator.RunCheck("Build", "dotnet build", "Build succeeded.", 0)
            };

            var result = _orchestrator.AggregateResult("verify-fmt", null, checks);
            string formatted = VerificationOrchestrator.FormatResultForCli(result);

            Assert.Contains("VERDICT: Pass", formatted);
            Assert.Contains("Build", formatted);
        }

        /// <summary>
        /// Partial 판정: 건너뛴 체크가 있으면 Partial
        /// </summary>
        [Fact]
        public void Verifier_PartialWhenSkippedCheckExists()
        {
            var checks = new List<VerificationCheck>
            {
                _orchestrator.RunCheck("Build", "dotnet build", "Build succeeded.", 0),
                _orchestrator.SkipCheck("Linux Test", "bash test.sh", "Not applicable")
            };

            var result = _orchestrator.AggregateResult("verify-partial", null, checks);
            Assert.Equal(VerificationVerdict.Partial, result.Verdict);
        }

        /// <summary>
        /// 증거 파일이 존재하지 않으면 Partial로 판정
        /// </summary>
        [Fact]
        public void Verifier_MissingEvidenceFileResultsInPartial()
        {
            var check = _orchestrator.RunCheck("Build", "dotnet build", "Build succeeded.", 0,
                evidenceFilePath: "nonexistent/build-log.txt");
            Assert.Equal(VerificationVerdict.Partial, check.Result);
            Assert.Contains("증거 파일이 누락", check.Notes);
        }
    }
}
