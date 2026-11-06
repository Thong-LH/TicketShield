using Microsoft.AspNetCore.Mvc;
using MockOrganizer.API.Gate;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/gate")]
public class GateScanController(GateScanService scans) : ControllerBase
{
    // Từ chối vé là kết quả nghiệp vụ nên vẫn trả 200. Chỉ dữ liệu gửi lên sai mới trả 400.
    [HttpPost("scan")]
    public async Task<IActionResult> Scan([FromBody] GateScanRequest request, CancellationToken cancellationToken)
    {
        if (!GateScanService.IsValid(request))
        {
            return BadRequest(new { message = "scanId and ticketCode are required." });
        }

        return Ok(await scans.ScanAsync(request, cancellationToken));
    }
}
