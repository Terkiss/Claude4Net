using System;
using System.Collections.Generic;

namespace Claude4Net.SDK.Telemetry
{
    /// <summary>
    /// GitHub 스타일 일별 잔디 캘린더 히트맵 타일 DTO
    /// </summary>
    public class CalendarHeatmapTileDto
    {
        public string DateIso { get; set; } = string.Empty; // YYYY-MM-DD
        public long TotalTokens { get; set; }
        public long PromptTokens { get; set; }
        public long CompTokens { get; set; }
        public double TotalCostUsd { get; set; }
        public double AvgLatencyMs { get; set; }
        public int RequestCount { get; set; }
        public int IntensityLevel { get; set; } // 0: None, 1: Low, 2: Medium, 3: High, 4: Max
        public List<string> TopModels { get; set; } = new();
    }

    /// <summary>
    /// 특정 일자의 시간대별(Hourly 00~23시) 토큰/비용 집계 DTO
    /// </summary>
    public class HourlyUsageBucketDto
    {
        public int Hour { get; set; } // 0 ~ 23
        public string HourLabel { get; set; } = string.Empty; // "00:00", "01:00", ...
        public long TotalTokens { get; set; }
        public double TotalCostUsd { get; set; }
        public double AvgLatencyMs { get; set; }
        public int RequestCount { get; set; }
    }

    /// <summary>
    /// 프로젝트/에이전트별 토큰 점유율 DTO
    /// </summary>
    public class ProjectUsageShareDto
    {
        public string ProjectName { get; set; } = string.Empty;
        public long TotalTokens { get; set; }
        public double TotalCostUsd { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>
    /// Datadog/Jaeger 스타일 분산 추적 스팬 DTO
    /// </summary>
    public class RequestTraceSpanDto
    {
        public string SpanId { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string TraceId { get; set; } = string.Empty;
        public string? ParentSpanId { get; set; }
        public string ComponentName { get; set; } = string.Empty; // e.g. "API Gateway", "Orchestrator", "LLM Call", "Vector DB"
        public string OperationName { get; set; } = string.Empty;
        public long StartTimeTicks { get; set; }
        public double DurationMs { get; set; }
        public string Status { get; set; } = "Success"; // "Success", "Running", "Failed"
        public string? Details { get; set; }
    }

    /// <summary>
    /// 실시간 5초 텔레메트리 틱 DTO
    /// </summary>
    public class LiveTelemetryTickDto
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int ActiveAgentsCount { get; set; }
        public long TodayTotalTokens { get; set; }
        public double TodayTotalCostUsd { get; set; }
        public double CurrentLatencyMs { get; set; }
        public double ThroughputTokensPerSec { get; set; }
        public int ApprovalsBadgeCount { get; set; }
        public List<double> RecentLatencySparkline { get; set; } = new();
        public List<double> RecentTokensSparkline { get; set; } = new();
    }

    /// <summary>
    /// 마스터 보안 승인 큐 대기 항목 DTO
    /// </summary>
    public class MasterApprovalItemDto
    {
        public string TaskId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string RequestedBy { get; set; } = "AGY Worker";
        public string RiskLevel { get; set; } = "Tier 3 - High Risk";
        public string TargetEnvironment { get; set; } = "Production / Local";
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public string? DiffSummary { get; set; }
        public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected"
    }
}
