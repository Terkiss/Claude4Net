using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 작업 조정(Coordination)의 단계를 정의하는 열거형입니다.
    /// </summary>
    public enum CoordinatePhase
    {
        /// <summary> 계획 단계 </summary>
        Planning,
        /// <summary> 실행 단계 </summary>
        Execution,
        /// <summary> 검증 단계 </summary>
        Verification,
        /// <summary> 완료됨 </summary>
        Completed,
        /// <summary> 실패함 </summary>
        Failed
    }

    /// <summary>
    /// 검토자의 결정 상태를 정의하는 열거형입니다.
    /// </summary>
    public enum ReviewerDecision
    {
        /// <summary> 대기 중 </summary>
        Pending,
        /// <summary> 승인됨 </summary>
        Approved,
        /// <summary> 거절됨 </summary>
        Rejected,
        /// <summary> 수정 요청 </summary>
        RequestChanges
    }

    /// <summary>
    /// 작업 진행의 근거(Evidence)를 기록하는 클래스입니다.
    /// </summary>
    public class CoordinateEvidence
    {
        /// <summary> 근거 고유 ID </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString().Substring(0, 8);
        /// <summary> 작성자 (에이전트 이름 등) </summary>
        public string Author { get; set; } = string.Empty;
        /// <summary> 기록 시간 </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        /// <summary> 기록 당시의 단계 </summary>
        public CoordinatePhase Phase { get; set; }
        /// <summary> 관련된 게이트 이름 </summary>
        public string GateName { get; set; } = string.Empty;
        /// <summary> 요약 내용 </summary>
        public string Summary { get; set; } = string.Empty;
        /// <summary> 상세 내용 (선택 사항) </summary>
        public string? Details { get; set; }
    }

    /// <summary>
    /// 단계 전환을 위해 통과해야 하는 검증 지점(Gate)을 정의합니다.
    /// </summary>
    public class CoordinateGate
    {
        /// <summary> 게이트 명칭 </summary>
        public string Name { get; set; } = string.Empty;
        /// <summary> 통과 여부 </summary>
        public bool IsPassed { get; set; }
        /// <summary> 통과를 위해 근거(Evidence) 기록이 필수인지 여부 </summary>
        public bool IsEvidenceRequired { get; set; } = true;
        /// <summary> 추가 의견 </summary>
        public string? Comments { get; set; }
        /// <summary> 마지막 업데이트 시간 </summary>
        public DateTime? UpdatedAt { get; set; }
        /// <summary> 연결된 근거 목록 </summary>
        public List<CoordinateEvidence> Evidences { get; set; } = new();
        /// <summary> 승인자 </summary>
        public string? ApprovedBy { get; set; }
    }

    /// <summary>
    /// 여러 에이전트 간의 협업 및 단계별 승인이 필요한 조정 작업을 관리하는 클래스입니다.
    /// </summary>
    public class CoordinateTask : TaskStateBase
    {
        /// <summary> 작업 제목 </summary>
        public string Title { get; set; } = string.Empty;
        /// <summary> 작업 상세 설명 </summary>
        public string Description { get; set; } = string.Empty;
        /// <summary> 현재 진행 단계 </summary>
        public CoordinatePhase CurrentPhase { get; set; } = CoordinatePhase.Planning;
        /// <summary> 구성된 검증 게이트 목록 </summary>
        public List<CoordinateGate> Gates { get; set; } = new();
        /// <summary> 검토 상태 </summary>
        public ReviewerDecision ReviewStatus { get; set; } = ReviewerDecision.Pending;
        /// <summary> 할당된 에이전트 명칭 </summary>
        public string? AssignedAgent { get; set; }
        /// <summary> 생성 일시 </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary> 마지막 수정 일시 </summary>
        public DateTime LastUpdatedAt { get; set; } = DateTime.Now;
        /// <summary> 작업 변경 이력 </summary>
        public List<string> History { get; set; } = new();
        
        /// <summary> 병합 준비 점수 (0~100) </summary>
        public double ReadinessScore { get; set; }
        /// <summary> 진행을 가로막는 요소(Blocker) 목록 </summary>
        public List<string> Blockers { get; set; } = new();

        public CoordinateTask()
        {
            Type = "Coordinate";
            Status = "Planning";
        }
    }
}
