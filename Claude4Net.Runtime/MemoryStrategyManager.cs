using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 메모리 전략 관리자입니다.
    /// 대화 이력에 적절한 전략을 적용하여 컨텍스트 윈도우에 맞는 메시지 목록을 구성합니다.
    /// </summary>
    public sealed class MemoryStrategyManager
    {
        private readonly Dictionary<MemoryStrategyType, IMemoryStrategy> _strategies = new();
        private MemoryConfig _config;

        /// <summary>
        /// 기본 전략들로 초기화된 매니저를 생성합니다.
        /// </summary>
        public static MemoryStrategyManager CreateWithDefaults(MemoryConfig? config = null)
        {
            var manager = new MemoryStrategyManager(config ?? new MemoryConfig());
            manager.RegisterStrategy(new FullHistoryStrategy());
            manager.RegisterStrategy(new SlidingWindowStrategy());
            manager.RegisterStrategy(new SummaryBasedStrategy());
            return manager;
        }

        public MemoryStrategyManager(MemoryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary> 현재 설정을 반환합니다. </summary>
        public MemoryConfig Config => _config;

        /// <summary>
        /// 전략을 등록합니다.
        /// </summary>
        public void RegisterStrategy(IMemoryStrategy strategy)
        {
            if (strategy == null) throw new ArgumentNullException(nameof(strategy));
            _strategies[strategy.Type] = strategy;
        }

        /// <summary>
        /// 설정을 업데이트합니다.
        /// </summary>
        public void UpdateConfig(MemoryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// 현재 설정에 따라 메모리 전략을 적용합니다.
        /// </summary>
        public MemoryWindow Apply(IReadOnlyList<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                return new MemoryWindow
                {
                    Messages = Array.Empty<ConversationMessage>(),
                    AppliedStrategy = _config.Strategy,
                    OriginalCount = 0,
                    RetainedCount = 0
                };
            }

            if (_strategies.TryGetValue(_config.Strategy, out var strategy))
            {
                return strategy.Apply(messages, _config);
            }

            // 전략이 등록되지 않은 경우 FullHistory 폴백
            return new MemoryWindow
            {
                Messages = messages,
                AppliedStrategy = MemoryStrategyType.FullHistory,
                OriginalCount = messages.Count,
                RetainedCount = messages.Count,
                EstimatedTotalTokens = messages.Sum(m => m.EstimatedTokens)
            };
        }

        /// <summary> 등록된 전략 수를 반환합니다. </summary>
        public int StrategyCount => _strategies.Count;
    }

    /// <summary>
    /// 전체 이력 유지 전략입니다.
    /// 모든 메시지를 그대로 유지합니다.
    /// </summary>
    public sealed class FullHistoryStrategy : IMemoryStrategy
    {
        public MemoryStrategyType Type => MemoryStrategyType.FullHistory;

        public MemoryWindow Apply(IReadOnlyList<ConversationMessage> messages, MemoryConfig config)
        {
            return new MemoryWindow
            {
                Messages = messages,
                AppliedStrategy = Type,
                OriginalCount = messages.Count,
                RetainedCount = messages.Count,
                EstimatedTotalTokens = messages.Sum(m => m.EstimatedTokens)
            };
        }
    }

    /// <summary>
    /// 슬라이딩 윈도우 전략입니다.
    /// 시스템 메시지와 핀된 메시지를 보존하고, 나머지는 최근 N개만 유지합니다.
    /// </summary>
    public sealed class SlidingWindowStrategy : IMemoryStrategy
    {
        public MemoryStrategyType Type => MemoryStrategyType.SlidingWindow;

        public MemoryWindow Apply(IReadOnlyList<ConversationMessage> messages, MemoryConfig config)
        {
            // 시스템 메시지와 핀된 메시지는 항상 보존
            var preserved = messages.Where(m => m.Role == "system" || m.IsPinned).ToList();

            // 나머지 메시지에서 최근 N개만 유지
            var remaining = messages.Where(m => m.Role != "system" && !m.IsPinned).ToList();
            var windowed = remaining.Skip(Math.Max(0, remaining.Count - config.WindowSize)).ToList();

            var result = preserved.Concat(windowed).ToList();

            return new MemoryWindow
            {
                Messages = result.AsReadOnly(),
                AppliedStrategy = Type,
                OriginalCount = messages.Count,
                RetainedCount = result.Count,
                SummarizedCount = messages.Count - result.Count,
                EstimatedTotalTokens = result.Sum(m => m.EstimatedTokens)
            };
        }
    }

    /// <summary>
    /// 요약 기반 전략입니다.
    /// 오래된 메시지를 요약 메시지로 대체합니다.
    /// </summary>
    public sealed class SummaryBasedStrategy : IMemoryStrategy
    {
        public MemoryStrategyType Type => MemoryStrategyType.SummaryBased;

        public MemoryWindow Apply(IReadOnlyList<ConversationMessage> messages, MemoryConfig config)
        {
            if (messages.Count <= config.WindowSize)
            {
                return new MemoryWindow
                {
                    Messages = messages,
                    AppliedStrategy = Type,
                    OriginalCount = messages.Count,
                    RetainedCount = messages.Count,
                    EstimatedTotalTokens = messages.Sum(m => m.EstimatedTokens)
                };
            }

            // 시스템 메시지 보존
            var systemMessages = messages.Where(m => m.Role == "system").Take(config.SystemMessageReserve).ToList();

            // 오래된 메시지를 요약으로 대체
            var oldMessages = messages.Where(m => m.Role != "system")
                .Take(messages.Count(m => m.Role != "system") - config.WindowSize)
                .ToList();

            var summaryText = $"[이전 대화 요약: {oldMessages.Count}개 메시지 요약됨]";
            var summaryMessage = new ConversationMessage
            {
                Role = "system",
                Content = summaryText,
                Timestamp = oldMessages.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow,
                EstimatedTokens = 50 // 요약 토큰 예상치
            };

            // 최근 메시지 유지
            var recentMessages = messages.Where(m => m.Role != "system")
                .Skip(Math.Max(0, messages.Count(m => m.Role != "system") - config.WindowSize))
                .ToList();

            var result = systemMessages
                .Append(summaryMessage)
                .Concat(recentMessages)
                .ToList();

            return new MemoryWindow
            {
                Messages = result.AsReadOnly(),
                AppliedStrategy = Type,
                OriginalCount = messages.Count,
                RetainedCount = result.Count,
                SummarizedCount = oldMessages.Count,
                EstimatedTotalTokens = result.Sum(m => m.EstimatedTokens)
            };
        }
    }
}
