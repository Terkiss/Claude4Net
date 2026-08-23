using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Claude4Net.SDK.Telemetry
{
    public interface ITeruTeruPandasTelemetryEngine
    {
        Task RecordTokenUsageAsync(
            string sessionId,
            string projectName,
            string provider,
            string model,
            int promptTokens,
            int compTokens,
            double latencyMs,
            DateTime? timestamp = null,
            CancellationToken ct = default);

        Task RecordTraceSpanAsync(
            RequestTraceSpanDto span,
            CancellationToken ct = default);

        Task<List<CalendarHeatmapTileDto>> GetCalendarHeatmapAsync(
            int days = 90,
            TimeSpan? timezoneOffset = null,
            CancellationToken ct = default);

        Task<List<CalendarHeatmapTileDto>> GetMonthUsageAsync(
            int year,
            int month,
            TimeSpan? timezoneOffset = null,
            CancellationToken ct = default);

        Task<List<HourlyUsageBucketDto>> GetHourlyDrilldownAsync(
            DateTime date,
            TimeSpan? timezoneOffset = null,
            CancellationToken ct = default);

        Task<List<ProjectUsageShareDto>> GetProjectSharesAsync(
            DateTime? fromDate = null,
            CancellationToken ct = default);

        Task<List<RequestTraceSpanDto>> GetRecentTracesAsync(
            int count = 10,
            CancellationToken ct = default);

        Task<LiveTelemetryTickDto> GetLiveTickAsync(
            CancellationToken ct = default);

        Task<int> GetPendingApprovalsCountAsync(
            CancellationToken ct = default);

        Task<List<MasterApprovalItemDto>> GetPendingApprovalsAsync(
            CancellationToken ct = default);

        Task<bool> ApproveTaskAsync(
            string taskId,
            string approver = "Master",
            CancellationToken ct = default);

        Task<bool> RejectTaskAsync(
            string taskId,
            string reason = "Rejected by Master",
            CancellationToken ct = default);
    }
}
