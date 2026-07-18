using System;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 자율 연속 실행 루프(Autonomous Continuation Loop)의 목표 상태를 정의합니다.
    /// !goal 명령으로 설정되며, GoalDispatcher가 각 턴 종료 후 진행 여부를 판단합니다.
    /// </summary>
    public sealed class GoalState
    {
        /// <summary>목표 고유 식별자</summary>
        public string Id { get; init; } = Guid.NewGuid().ToString();

        /// <summary>목표 지시문 (사용자가 입력한 objective)</summary>
        public string Objective { get; init; } = string.Empty;

        /// <summary>현재 목표 상태</summary>
        public GoalStatus Status { get; set; } = GoalStatus.Active;

        /// <summary>생성 시각</summary>
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

        /// <summary>실행된 턴 수</summary>
        public int TurnCount { get; set; } = 0;

        /// <summary>최대 허용 턴 수 (예산). 0 = 무제한</summary>
        public int MaxTurns { get; init; } = 25;

        /// <summary>진행 없이 연속으로 종료된 턴 수 (무한 루프 방지용)</summary>
        public int NoProgressCount { get; set; } = 0;

        /// <summary>진행 없이 허용할 최대 연속 턴 수. 이 값을 초과하면 자동 정지</summary>
        public int MaxNoProgressTurns { get; init; } = 3;

        /// <summary>마지막 턴에서 실행된 도구 호출 수 (의미 있는 작업 판단용)</summary>
        public int LastTurnToolCallCount { get; set; } = 0;

        /// <summary>마지막 턴의 응답 텍스트 길이</summary>
        public int LastTurnResponseLength { get; set; } = 0;

        /// <summary>목표 달성 여부 (에이전트가 스스로 선언하거나 사용자가 판단)</summary>
        public bool IsCompleted => Status == GoalStatus.Completed;

        /// <summary>목표가 활성 상태인지 여부</summary>
        public bool IsActive => Status == GoalStatus.Active;
    }

    /// <summary>
    /// 목표의 실행 상태를 나타냅니다.
    /// </summary>
    public enum GoalStatus
    {
        /// <summary>활성 — 자율 루프가 실행 중</summary>
        Active,
        /// <summary>완료 — 목표 달성</summary>
        Completed,
        /// <summary>정지 — 사용자가 수동 정지 또는 예산 소진</summary>
        Stopped,
        /// <summary>실패 — 무한 루프 감지 또는 치명적 오류</summary>
        Failed
    }
}
