using Microsoft.AspNetCore.Mvc;
using Claude4Net.Runtime;
using Claude4Net.Dashboard.Hubs;
using Claude4Net.SDK;
using System.Threading.Tasks;
using System.Linq;

namespace Claude4Net.Dashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsageController : ControllerBase
    {
        [HttpGet("{sessionId?}")]
        public async Task<IActionResult> GetUsage([FromRoute] string? sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = AppState.SessionId;
            }

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return BadRequest("No active session ID.");
            }

            // Validate sessionId
            if (sessionId.Contains("..") || sessionId.Contains("/") || sessionId.Contains("\\") || sessionId.Contains(":"))
            {
                return BadRequest("Invalid session ID.");
            }

            try
            {
                string ws = AppState.CurrentCwd ?? AppState.SystemBaseDir ?? System.AppDomain.CurrentDomain.BaseDirectory;
                var eventStore = new FileAgentEventStore(ws);
                var projectionEngine = new EventProjectionEngine(eventStore);
                var usageProjection = new UsageProjection();
                projectionEngine.RegisterProjection(usageProjection);
                await projectionEngine.RebuildAsync(sessionId);

                var model = usageProjection.Model;
                var dto = new UsageReadModelDto
                {
                    SessionId = sessionId,
                    TotalCalls = model.TotalCalls,
                    TotalInputTokens = model.TotalInputTokens,
                    TotalOutputTokens = model.TotalOutputTokens,
                    TotalCost = model.TotalCost,
                    LatencyEma = model.LatencyEma,
                    ModelMetrics = model.ModelMetrics.Values.Select(m => new ModelUsageMetricsDto
                    {
                        Provider = m.Provider,
                        Model = m.Model,
                        CallCount = m.CallCount,
                        InputTokens = m.InputTokens,
                        OutputTokens = m.OutputTokens,
                        LatencyEma = m.LatencyEma,
                        AccumulatedCost = m.AccumulatedCost
                    }).ToList()
                };

                return Ok(dto);
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
