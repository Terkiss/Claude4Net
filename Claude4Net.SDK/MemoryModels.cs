using System;
using System.Collections.Generic;

namespace Claude4Net.SDK
{
    /// <summary>
    /// 메모리 전략 유형을 정의합니다.
    /// </summary>
    public enum MemoryStrategyType
    {
        /// <summary> 전체 이력 유지 (컨텍스트 윈도우 초과 시 위험) </summary>
        FullHistory,
        /// <summary> 슬라이딩 윈도우 — 최근 N개 메시지만 유지 </summary>
        SlidingWindow,
        /// <summary> 요약 기반 — 오래된 이력을 요약으로 대체 </summary>
        SummaryBased,
        /// <summary> 하이브리드 — 중요 메시지 + 최근 윈도우 + 나머지 요약 </summary>
        Hybrid
    }

    /// <summary>
    /// 메모리 전략 설정입니다.
    /// </summary>
    public sealed record MemoryConfig
    {
        /// <summary> 활성 전략 유형 </summary>
        public MemoryStrategyType Strategy { get; init; } = MemoryStrategyType.SlidingWindow;
        /// <summary> 슬라이딩 윈도우 크기 (메시지 수) </summary>
        public int WindowSize { get; init; } = 20;
        /// <summary> 요약 시 보존할 시스템 메시지 수 </summary>
        public int SystemMessageReserve { get; init; } = 2;
        /// <summary> 컨텍스트 윈도우 제한 (토큰 수) </summary>
        public int MaxTokenBudget { get; init; } = 100_000;
    }

    /// <summary>
    /// 대화 메시지 항목입니다.
    /// </summary>
    public sealed class ConversationMessage
    {
        /// <summary> 메시지 역할 (system, user, assistant, tool) </summary>
        public string Role { get; init; } = string.Empty;
        /// <summary> 메시지 내용 </summary>
        public string Content { get; init; } = string.Empty;
        /// <summary> 생성 시간 </summary>
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        /// <summary> 중요도 표시 (pin된 메시지는 슬라이딩 윈도우에서 제외) </summary>
        public bool IsPinned { get; init; }
        /// <summary> 예상 토큰 수 </summary>
        public int EstimatedTokens { get; init; }
    }

    /// <summary>
    /// 메모리 전략이 적용된 결과입니다.
    /// </summary>
    public sealed class MemoryWindow
    {
        /// <summary> 유지된 메시지 목록 </summary>
        public IReadOnlyList<ConversationMessage> Messages { get; init; } = Array.Empty<ConversationMessage>();
        /// <summary> 적용된 전략 유형 </summary>
        public MemoryStrategyType AppliedStrategy { get; init; }
        /// <summary> 원본 메시지 수 </summary>
        public int OriginalCount { get; init; }
        /// <summary> 유지된 메시지 수 </summary>
        public int RetainedCount { get; init; }
        /// <summary> 요약으로 대체된 메시지 수 </summary>
        public int SummarizedCount { get; init; }
        /// <summary> 예상 총 토큰 수 </summary>
        public int EstimatedTotalTokens { get; init; }
    }

    /// <summary>
    /// 메모리 전략 인터페이스입니다.
    /// </summary>
    public interface IMemoryStrategy
    {
        /// <summary> 전략 유형 </summary>
        MemoryStrategyType Type { get; }

        /// <summary>
        /// 대화 이력에 메모리 전략을 적용하여 컨텍스트 윈도우에 맞는 메시지 목록을 반환합니다.
        /// </summary>
        MemoryWindow Apply(IReadOnlyList<ConversationMessage> messages, MemoryConfig config);
    }
}
