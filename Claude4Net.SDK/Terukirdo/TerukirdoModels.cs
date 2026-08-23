using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.SDK.Terukirdo
{
    /// <summary>
    /// 테르키르도(Terukirdo)의 4대 운영 모드
    /// </summary>
    public enum TerukirdoMode
    {
        /// <summary> Tier 0: 일상 대화, 감정 보좌, 아이디어 브레인스토밍 (No Code Execution) </summary>
        Companion,

        /// <summary> Tier 0: 일정, 작업 목록 정리, 문서 요약 (Maid Secretary) </summary>
        MaidSecretary,

        /// <summary> Tier 1~2: 자율 개발 계획 수립 및 멀티 에이전트 오케스트레이션 (Active Orchestrator) </summary>
        Orchestrator,

        /// <summary> Tier 3: 최종 관제 및 위험 작업 전담 감사 (Final Controller Mode) </summary>
        FinalController
    }

    /// <summary>
    /// 작업 위험도 및 복잡도에 따른 적응형 실행 티어 (Adaptive Loop Tier)
    /// </summary>
    public enum AdaptiveLoopTier
    {
        /// <summary> Tier 0: 일상 대화/요약 (Ralph Loop 비활성화, 순수 LLM 스트리밍) </summary>
        Tier0_Companion = 0,

        /// <summary> Tier 1: 단순 오탈자, 격리된 단일 파일 수정 (AGY Worker 직접 검증) </summary>
        Tier1_LowRisk = 1,

        /// <summary> Tier 2: 일반 기능 구현, 멀티 파일 수정, API 연동 (Ralph Loop: Plan -> Worker -> Review -> Judge -> UFC -> FAC) </summary>
        Tier2_MediumRisk_RalphLoop = 2,

        /// <summary> Tier 3: 인증/보안, DB migration, 데이터 삭제, 배포 (전체 Ralph Loop + 주인님 Checkpoint 승인 강제) </summary>
        Tier3_HighRisk_Release = 3
    }

    /// <summary>
    /// 테르키르도 실행 컨텍스트
    /// </summary>
    public class TerukirdoContext
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public string WorkspacePath { get; set; } = string.Empty;
        public string CurrentProvider { get; set; } = "Default";
        public string CurrentModel { get; set; } = "Default";
        public TerukirdoMode Mode { get; set; } = TerukirdoMode.Orchestrator;
        public AdaptiveLoopTier? ExplicitTier { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// 테르키르도 오케스트레이션 실행 결과
    /// </summary>
    public class TerukirdoExecutionResult
    {
        public bool IsSuccess { get; set; }
        public string Output { get; set; } = string.Empty;
        public TerukirdoMode ModeUsed { get; set; }
        public AdaptiveLoopTier TierUsed { get; set; }
        public List<string> ExecutedSubagents { get; set; } = new();
        public string? BlockedReason { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// 테르키르도 상태 요약
    /// </summary>
    public class TerukirdoStatusSummary
    {
        public string ProtocolVersion { get; set; } = "v5.4";
        public TerukirdoMode CurrentMode { get; set; } = TerukirdoMode.Orchestrator;
        public AdaptiveLoopTier DefaultTier { get; set; } = AdaptiveLoopTier.Tier2_MediumRisk_RalphLoop;
        public bool PrimeDirectiveActive { get; set; } = true;
        public int InterceptedViolationsCount { get; set; }
        public DateTime LastMemorySyncTime { get; set; } = DateTime.UtcNow;
        public string ActiveWorkspace { get; set; } = string.Empty;
    }
}
