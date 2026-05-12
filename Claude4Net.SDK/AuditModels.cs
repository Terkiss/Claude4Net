using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 감사 이벤트 종류를 정의합니다.
    /// </summary>
    public enum AuditCategory
    {
        /// <summary> 라우팅 의사결정 </summary>
        Routing,
        /// <summary> 도구 실행 </summary>
        ToolExecution,
        /// <summary> 권한 평가 </summary>
        Permission,
        /// <summary> 검증 게이트 </summary>
        Verification,
        /// <summary> 메모리 전략 적용 </summary>
        MemoryStrategy,
        /// <summary> 훅 실행 </summary>
        HookExecution,
        /// <summary> 세션 관리 </summary>
        SessionManagement,
        /// <summary> 보안 이벤트 </summary>
        Security
    }

    /// <summary>
    /// 감사 심각도 수준입니다.
    /// </summary>
    public enum AuditSeverity
    {
        /// <summary> 참고 정보 </summary>
        Info,
        /// <summary> 경고 </summary>
        Warning,
        /// <summary> 중요 </summary>
        Critical
    }

    /// <summary>
    /// 감사 추적 항목입니다.
    /// 에이전트의 의사결정과 행동을 기록합니다.
    /// </summary>
    public sealed class AuditEntry
    {
        /// <summary> 고유 ID </summary>
        public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
        /// <summary> 기록 시간 </summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        /// <summary> 감사 카테고리 </summary>
        public AuditCategory Category { get; init; }
        /// <summary> 심각도 </summary>
        public AuditSeverity Severity { get; init; } = AuditSeverity.Info;
        /// <summary> 세션 ID </summary>
        public string? SessionId { get; init; }
        /// <summary> 행위자 (agent, user, system) </summary>
        public string Actor { get; init; } = "agent";
        /// <summary> 행동 설명 </summary>
        public string Action { get; init; } = string.Empty;
        /// <summary> 결과 </summary>
        public string? Outcome { get; init; }
        /// <summary> 추가 메타데이터 </summary>
        public Dictionary<string, string> Metadata { get; init; } = new();
    }
}
