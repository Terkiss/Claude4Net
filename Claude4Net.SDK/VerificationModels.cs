using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 검증 결과의 최종 판정을 나타냅니다.
    /// Default-fail 정책: 모든 체크는 Fail로 시작하며, 
    /// 명령 출력 또는 증거 파일이 확인된 경우에만 Pass로 전환됩니다.
    /// </summary>
    public enum VerificationVerdict
    {
        /// <summary>
        /// 모든 필수 체크가 증거와 함께 통과됨
        /// </summary>
        Pass,

        /// <summary>
        /// 하나 이상의 필수 체크가 실패함
        /// </summary>
        Fail,

        /// <summary>
        /// 일부 체크가 통과했으나 건너뛴 체크 또는 증거 없는 체크가 존재함
        /// </summary>
        Partial
    }

    /// <summary>
    /// 개별 검증 체크의 결과를 나타내는 레코드입니다.
    /// 각 체크는 명령 실행 후 출력 기반으로 판정됩니다.
    /// </summary>
    public sealed record VerificationCheck
    {
        /// <summary>
        /// 체크의 이름 (예: "Standard Build", "Unit Tests")
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// 실행할 명령 (예: "dotnet build")
        /// </summary>
        public string Command { get; init; } = string.Empty;

        /// <summary>
        /// 명령 출력이 저장된 파일 경로 (상대 경로, 증거 디렉토리 기준)
        /// </summary>
        public string? OutputFile { get; init; }

        /// <summary>
        /// 체크 결과 판정 (default-fail: 초기값은 항상 Fail)
        /// </summary>
        public VerificationVerdict Result { get; init; } = VerificationVerdict.Fail;

        /// <summary>
        /// 결과를 뒷받침하는 증거 요약 (명령 출력의 핵심 부분)
        /// </summary>
        public string? Evidence { get; init; }

        /// <summary>
        /// 추가 설명 또는 건너뛴 이유
        /// </summary>
        public string? Notes { get; init; }

        /// <summary>
        /// 체크가 명시적으로 건너뛰어졌는지 여부
        /// </summary>
        public bool Skipped { get; init; }

        /// <summary>
        /// 체크 실행 시작 시각
        /// </summary>
        public DateTimeOffset? StartedAt { get; init; }

        /// <summary>
        /// 체크 실행 완료 시각
        /// </summary>
        public DateTimeOffset? CompletedAt { get; init; }
    }

    /// <summary>
    /// 전체 검증 결과를 담는 레코드입니다.
    /// .claude4net/sessions/{id}/verification-result.json에 저장됩니다.
    /// </summary>
    public sealed record VerificationResult
    {
        /// <summary>
        /// 검증 세션 ID
        /// </summary>
        public string VerifierSessionId { get; init; } = string.Empty;

        /// <summary>
        /// 검증 대상인 생성자 세션 ID
        /// </summary>
        public string? GeneratorSessionId { get; init; }

        /// <summary>
        /// 전체 판정 (모든 체크 Pass → Pass, 하나라도 Fail → Fail, 건너뛴 체크 존재 → Partial)
        /// </summary>
        public VerificationVerdict Verdict { get; init; } = VerificationVerdict.Fail;

        /// <summary>
        /// 개별 체크 결과 목록
        /// </summary>
        public IReadOnlyList<VerificationCheck> Checks { get; init; } = Array.Empty<VerificationCheck>();

        /// <summary>
        /// 검증 실행 시각
        /// </summary>
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 스키마 버전
        /// </summary>
        public string SchemaVersion { get; init; } = "1.0";
    }

    /// <summary>
    /// 검증 전용 세션의 메타데이터를 담는 레코드입니다.
    /// 검증 세션은 생성자 컨텍스트를 상속하지 않으며 읽기 전용으로 실행됩니다.
    /// </summary>
    public sealed record VerifierSessionRecord
    {
        /// <summary>
        /// 검증 세션 고유 ID
        /// </summary>
        public string VerifierSessionId { get; init; } = string.Empty;

        /// <summary>
        /// 검증 대상인 생성자 세션 ID
        /// </summary>
        public string? GeneratorSessionId { get; init; }

        /// <summary>
        /// 읽기 전용 모드 (검증 세션은 항상 true)
        /// </summary>
        public bool ReadOnlyMode { get; init; } = true;

        /// <summary>
        /// 세션 생성 시각
        /// </summary>
        public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// 워크스페이스 경로
        /// </summary>
        public string WorkspacePath { get; init; } = string.Empty;
    }
}
