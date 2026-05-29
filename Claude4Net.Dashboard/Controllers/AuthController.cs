using Microsoft.AspNetCore.Mvc;
using Claude4Net.Runtime.Security;
using System.Threading.Tasks;

namespace Claude4Net.Dashboard.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        public class LanAuthRequestDto
        {
            public string DeviceName { get; set; } = "";
            public string AppInstanceId { get; set; } = "";
        }

        [HttpPost("lan")]
        public async Task<IActionResult> LanAuth([FromBody] LanAuthRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DeviceName) || string.IsNullOrWhiteSpace(request.AppInstanceId))
            {
                return BadRequest("Invalid LAN authorization parameters.");
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            // Map IPv6 loopback to IPv4 loopback for cleaner checking if needed,
            // though IPAddress.IsLoopback handles both.
            if (clientIp == "::1")
            {
                clientIp = "127.0.0.1";
            }

            var result = await PairingManager.AuthorizeLanAsync(request.DeviceName, request.AppInstanceId, clientIp);

            if (!result.Success)
            {
                // Return 401 Unauthorized for access denied or timeout
                return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized, result.Message);
            }

            return Ok(result.Token);
        }
    }
}
