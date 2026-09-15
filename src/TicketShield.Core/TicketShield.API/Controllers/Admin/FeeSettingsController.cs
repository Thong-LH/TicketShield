using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Commands.UpdateFeeSettings;
using TicketShield.Application.Features.Admin.FeeSettings.Models;
using TicketShield.Application.Features.Admin.FeeSettings.Queries.GetFeeSettings;

namespace TicketShield.API.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("api/v1/admin/fee-settings")]
public class FeeSettingsController : ApiControllerBase
{
    /// <summary>
    /// BE-CORE-2.6.2: Retrieve current dynamic system resale fee configuration (Admin only)
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResaleFeeConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFeeSettings()
    {
        var result = await Mediator.Send(new GetFeeSettingsQuery());
        return Ok(result);
    }

    /// <summary>
    /// BE-CORE-2.6.2: Update dynamic system resale fee configuration &amp; invalidate fee cache (Admin only)
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateFeeSettings([FromBody] UpdateFeeSettingsCommand command)
    {
        var result = await Mediator.Send(command);
        return Ok(result);
    }
}
