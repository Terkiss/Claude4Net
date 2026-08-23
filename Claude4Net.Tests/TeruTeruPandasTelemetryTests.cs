using System;
using System.Linq;
using System.Threading.Tasks;
using Claude4Net.Runtime.Telemetry;
using Claude4Net.SDK.Telemetry;
using Claude4Net.Dashboard.Hubs;
using Xunit;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class TeruTeruPandasTelemetryTests
    {
        [Fact]
        public void PricingEngine_CalculatesCost_For2026Models()
        {
            var engine = new PricingEngine();

            // Gemini 3.7 Flash: 1M prompt = $0.10, 1M comp = $0.40
            double costFlash = engine.CalculateCost("gemini-3.7-flash", 100_000, 50_000);
            Assert.Equal(0.03, costFlash, 4);

            // Claude 3.7 Sonnet: 1M prompt = $3.00, 1M comp = $15.00
            double costSonnet = engine.CalculateCost("claude-3-7-sonnet", 10_000, 2_000);
            Assert.Equal(0.06, costSonnet, 4);

            // DeepSeek-V3: 1M prompt = $0.14, 1M comp = $0.28
            double costDeepSeek = engine.CalculateCost("deepseek-v3", 1_000_000, 1_000_000);
            Assert.Equal(0.42, costDeepSeek, 4);
        }

        [Fact]
        public async Task TelemetryEngine_Records_And_Retrieves_Heatmap()
        {
            var engine = new TeruTeruPandasTelemetryEngine();

            var now = DateTime.UtcNow;
            await engine.RecordTokenUsageAsync(
                sessionId: "test-session-01",
                projectName: "Claude4Net-App",
                provider: "Anthropic",
                model: "claude-3-7-sonnet",
                promptTokens: 5000,
                compTokens: 1000,
                latencyMs: 125.5,
                timestamp: now
            );

            var heatmap = await engine.GetCalendarHeatmapAsync(90, TimeSpan.FromHours(9));
            Assert.NotEmpty(heatmap);
            Assert.True(heatmap.Count >= 90);

            var todayTile = heatmap.Last();
            Assert.True(todayTile.TotalTokens > 0);
            Assert.True(todayTile.TotalCostUsd > 0);
            Assert.True(todayTile.IntensityLevel >= 1);
        }

        [Fact]
        public async Task TelemetryEngine_Retrieves_HourlyDrilldown_24Buckets()
        {
            var engine = new TeruTeruPandasTelemetryEngine();

            var now = DateTime.UtcNow;
            var hourly = await engine.GetHourlyDrilldownAsync(now, TimeSpan.FromHours(9));

            Assert.NotNull(hourly);
            Assert.Equal(24, hourly.Count);
            Assert.Equal("00:00", hourly[0].HourLabel);
            Assert.Equal("23:00", hourly[23].HourLabel);
        }

        [Fact]
        public async Task TelemetryEngine_Retrieves_ProjectShares_And_Traces()
        {
            var engine = new TeruTeruPandasTelemetryEngine();

            var shares = await engine.GetProjectSharesAsync();
            Assert.NotEmpty(shares);
            Assert.True(shares.Sum(s => s.Percentage) >= 99.0); // Sum ~100%

            var traces = await engine.GetRecentTracesAsync(5);
            Assert.NotEmpty(traces);
            Assert.NotNull(traces[0].TraceId);
            Assert.NotNull(traces[0].ComponentName);
        }

        [Fact]
        public async Task TelemetryEngine_Handles_MasterApprovals()
        {
            var engine = new TeruTeruPandasTelemetryEngine();

            int pendingCount = await engine.GetPendingApprovalsCountAsync();
            Assert.True(pendingCount >= 1);

            var list = await engine.GetPendingApprovalsAsync();
            Assert.NotEmpty(list);
            var firstTask = list.First(a => a.Status == "Pending");

            // Approve task
            bool approved = await engine.ApproveTaskAsync(firstTask.TaskId, "Master");
            Assert.True(approved);

            var updatedList = await engine.GetPendingApprovalsAsync();
            var updatedTask = updatedList.First(a => a.TaskId == firstTask.TaskId);
            Assert.Equal("Approved", updatedTask.Status);
        }

        [Fact]
        public async Task ControlPlaneHub_Exposes_Telemetry_Endpoints()
        {
            var hub = new ControlPlaneHub();

            var heatmap = await hub.GetCalendarHeatmap(30);
            Assert.NotEmpty(heatmap);

            var hourly = await hub.GetHourlyDrilldown(DateTime.UtcNow.ToString("yyyy-MM-dd"));
            Assert.Equal(24, hourly.Count);

            var shares = await hub.GetProjectShares();
            Assert.NotEmpty(shares);

            var traces = await hub.GetRecentTraces(5);
            Assert.NotEmpty(traces);

            var tick = await hub.GetLiveTelemetryTick();
            Assert.NotNull(tick);
            Assert.True(tick.TodayTotalTokens > 0);
            Assert.True(tick.TodayTotalCostUsd > 0);
        }
    }
}
