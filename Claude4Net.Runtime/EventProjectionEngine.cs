using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.SDK;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// CQRS 프로젝션 엔진입니다.
    /// 이벤트 스트림을 읽어 등록된 프로젝션에 적용하고, 읽기 모델을 구축합니다.
    /// </summary>
    public sealed class EventProjectionEngine
    {
        private readonly IAgentEventStore _eventStore;
        private readonly List<IEventProjection> _projections = new();
        private long _lastProcessedVersion;

        /// <summary>
        /// 프로젝션 엔진을 초기화합니다.
        /// </summary>
        /// <param name="eventStore">이벤트를 읽을 이벤트 스토어</param>
        public EventProjectionEngine(IAgentEventStore eventStore)
        {
            _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        }

        /// <summary>
        /// 프로젝션을 등록합니다.
        /// </summary>
        public EventProjectionEngine RegisterProjection(IEventProjection projection)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            _projections.Add(projection);
            return this;
        }

        /// <summary>
        /// 등록된 모든 프로젝션을 반환합니다.
        /// </summary>
        public IReadOnlyList<IEventProjection> Projections => _projections.AsReadOnly();

        /// <summary>
        /// 이벤트 스트림을 재생하여 모든 프로젝션을 구축합니다.
        /// </summary>
        /// <param name="sessionId">재생할 세션 ID</param>
        /// <param name="fromVersion">시작 버전 (기본값: 0, 처음부터)</param>
        public async Task ReplayAsync(string sessionId, long fromVersion = 0)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                throw new ArgumentException("Session ID cannot be empty.", nameof(sessionId));

            var events = await _eventStore.GetEventsAsync(sessionId, fromVersion);

            foreach (var @event in events)
            {
                foreach (var projection in _projections)
                {
                    projection.Apply(@event);
                }
                _lastProcessedVersion = @event.Version;
            }
        }

        /// <summary>
        /// 모든 프로젝션을 초기화하고 처음부터 재구축합니다.
        /// </summary>
        /// <param name="sessionId">재구축할 세션 ID</param>
        public async Task RebuildAsync(string sessionId)
        {
            foreach (var projection in _projections)
            {
                projection.Reset();
            }
            _lastProcessedVersion = 0;
            await ReplayAsync(sessionId, 0);
        }

        /// <summary>
        /// 마지막으로 처리한 버전 이후의 새 이벤트만 적용합니다 (증분 업데이트).
        /// </summary>
        /// <param name="sessionId">세션 ID</param>
        public async Task CatchUpAsync(string sessionId)
        {
            await ReplayAsync(sessionId, _lastProcessedVersion);
        }

        /// <summary>
        /// 타입으로 특정 프로젝션을 가져옵니다.
        /// </summary>
        public T? GetProjection<T>() where T : class, IEventProjection
        {
            return _projections.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// 마지막으로 처리된 이벤트 버전을 반환합니다.
        /// </summary>
        public long LastProcessedVersion => _lastProcessedVersion;

        /// <summary>
        /// 인메모리 이벤트 목록으로부터 직접 프로젝션을 구축합니다 (테스트용).
        /// </summary>
        public void ApplyEvents(IEnumerable<IAgentEvent> events)
        {
            foreach (var @event in events)
            {
                foreach (var projection in _projections)
                {
                    projection.Apply(@event);
                }
                _lastProcessedVersion = @event.Version;
            }
        }
    }
}
