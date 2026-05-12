using System;
using System.Collections.Generic;
using System.Linq;
using Claude4Net.SDK;

namespace Claude4Net.Runtime
{
    /// <summary>
    /// 감사 추적 서비스입니다.
    /// 에이전트의 의사결정과 행동을 기록하고 조회할 수 있는 인메모리 감사 로그를 제공합니다.
    /// </summary>
    public sealed class AuditTrailService
    {
        private readonly List<AuditEntry> _entries = new();
        private readonly int _maxEntries;

        /// <summary>
        /// 감사 추적 서비스를 초기화합니다.
        /// </summary>
        /// <param name="maxEntries">최대 보관할 감사 항목 수 (기본값: 10000)</param>
        public AuditTrailService(int maxEntries = 10_000)
        {
            _maxEntries = maxEntries;
        }

        /// <summary>
        /// 감사 항목을 기록합니다.
        /// </summary>
        public void Record(AuditEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            _entries.Add(entry);

            // 최대 항목 수 초과 시 오래된 항목 제거 (순환 버퍼)
            while (_entries.Count > _maxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        /// <summary>
        /// 편의 메서드: 카테고리, 행동, 결과로 간편하게 기록합니다.
        /// </summary>
        public void Record(AuditCategory category, string action, string? outcome = null,
            AuditSeverity severity = AuditSeverity.Info, string? sessionId = null)
        {
            Record(new AuditEntry
            {
                Category = category,
                Action = action,
                Outcome = outcome,
                Severity = severity,
                SessionId = sessionId
            });
        }

        /// <summary>
        /// 모든 감사 항목을 반환합니다.
        /// </summary>
        public IReadOnlyList<AuditEntry> GetAll() => _entries.AsReadOnly();

        /// <summary>
        /// 카테고리로 필터링합니다.
        /// </summary>
        public IReadOnlyList<AuditEntry> GetByCategory(AuditCategory category)
        {
            return _entries.Where(e => e.Category == category).ToList().AsReadOnly();
        }

        /// <summary>
        /// 심각도로 필터링합니다.
        /// </summary>
        public IReadOnlyList<AuditEntry> GetBySeverity(AuditSeverity severity)
        {
            return _entries.Where(e => e.Severity == severity).ToList().AsReadOnly();
        }

        /// <summary>
        /// 세션 ID로 필터링합니다.
        /// </summary>
        public IReadOnlyList<AuditEntry> GetBySession(string sessionId)
        {
            return _entries.Where(e => e.SessionId == sessionId).ToList().AsReadOnly();
        }

        /// <summary>
        /// 시간 범위로 필터링합니다.
        /// </summary>
        public IReadOnlyList<AuditEntry> GetByTimeRange(DateTime from, DateTime to)
        {
            return _entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).ToList().AsReadOnly();
        }

        /// <summary>
        /// 감사 항목 수를 반환합니다.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// 모든 항목을 제거합니다.
        /// </summary>
        public void Clear() => _entries.Clear();
    }
}
