using Microsoft.AspNetCore.Mvc;
using Claude4Net.Runtime.Security;
using System.Threading.Tasks;

namespace Claude4Net.Dashboard.Controllers
{
    [ApiController]
    [Route("api/pairing")]
    public class PairingController : ControllerBase
    {
        public class PairingRequestDto
        {
            public string DeviceName { get; set; } = "";
            public string AppInstanceId { get; set; } = "";
        }

        public class PairingConfirmDto
        {
            public string PairingId { get; set; } = "";
            public string Code { get; set; } = "";
        }

        [HttpPost("request")]
        public async Task<IActionResult> RequestPairing([FromBody] PairingRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.DeviceName) || string.IsNullOrWhiteSpace(request.AppInstanceId))
            {
                return BadRequest("Invalid pairing request parameters.");
            }

            var result = await PairingManager.CreatePairingRequestAsync(request.DeviceName, request.AppInstanceId);
            return Ok(new
            {
                pairingId = result.PairingId,
                expiresAt = result.ExpiresAt
            });
        }

        [HttpPost("confirm")]
        public async Task<IActionResult> ConfirmPairing([FromBody] PairingConfirmDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PairingId) || string.IsNullOrWhiteSpace(request.Code))
            {
                return BadRequest("Invalid pairing confirmation parameters.");
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            var result = await PairingManager.ConfirmPairingAsync(request.PairingId, request.Code, clientIp);

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result.Token);
        }
    }
}
