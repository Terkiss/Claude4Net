using System;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.SDK.Terukirdo
{
    /// <summary>
    /// 최상위 메이드 오케스트레이터 테르키르도 인터페이스
    /// </summary>
    public interface ITerukirdoOrchestrator
    {
        /// <summary> 현재 활성화된 운영 모드 </summary>
        TerukirdoMode CurrentMode { get; }

        /// <summary> 현재 적용 중인 적응형 티어 </summary>
        AdaptiveLoopTier CurrentTier { get; }

        /// <summary> 운영 모드 수동 전환 </summary>
        void SetMode(TerukirdoMode mode);

        /// <summary> 기본 적응형 티어 수동 설정 </summary>
        void SetTier(AdaptiveLoopTier? tier);

        /// <summary> 사용자 입력 처리 및 서브에이전트 조율 실행 </summary>
        Task<TerukirdoExecutionResult> ProcessInputAsync(string input, TerukirdoContext context, CancellationToken ct = default);

        /// <summary> 메모리 및 궤적 수동 동기화 </summary>
        Task SyncMemoryAsync(CancellationToken ct = default);

        /// <summary> 테르키르도 런타임 상태 요약 조회 </summary>
        Task<TerukirdoStatusSummary> GetStatusAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 적응형 티어 라우터 인터페이스 (의도 분류 및 위험도 평가)
    /// </summary>
    public interface ITerukirdoTierRouter
    {
        AdaptiveLoopTier ClassifyIntent(string prompt, TerukirdoMode mode, AdaptiveLoopTier? explicitTier = null);
    }

    /// <summary>
    /// 이중 평면 메모리 & 궤적 관리 인터페이스
    /// </summary>
    public interface ITerukirdoMemoryService
    {
        /// <summary> 운영 궤적 이벤트 자동 기록 (docs/Terukirdo_Trajectory.txt) </summary>
        Task AppendTrajectoryEventAsync(string eventSummary, string rawEvidence, CancellationToken ct = default);

        /// <summary> 주인님 선호도 안전 기록 (docs/Terukirdo_memory.txt, Opt-In 확인 필수) </summary>
        Task SaveMasterPreferenceAsync(string key, string value, bool userOptInConfirmed, CancellationToken ct = default);

        /// <summary> 궤적 및 메모리 동기화 </summary>
        Task SyncAllAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// 프라임 디렉티브 안전 정책 인터셉터
    /// </summary>
    public interface ITerukirdoPrimeDirective
    {
        /// <summary> 명령어 또는 파일 작업이 프라임 디렉티브를 위반하는지 검증 </summary>
        PrimeDirectiveCheckResult ValidateAction(string actionType, string target, string? arguments = null);
    }

    public class PrimeDirectiveCheckResult
    {
        public bool IsAllowed { get; set; }
        public bool RequiresMasterApproval { get; set; }
        public string? ViolationReason { get; set; }

        public static PrimeDirectiveCheckResult Allowed() => new() { IsAllowed = true };
        public static PrimeDirectiveCheckResult Blocked(string reason) => new() { IsAllowed = false, ViolationReason = reason };
        public static PrimeDirectiveCheckResult RequiresApproval(string reason) => new() { IsAllowed = true, RequiresMasterApproval = true, ViolationReason = reason };
    }
}
