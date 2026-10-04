using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Payouts.Queries.GetMyPayouts;

namespace TicketShield.API.Controllers;

[Authorize]
[Route("api/v1/payouts")]
public class PayoutsController : ApiControllerBase
{
    [HttpGet("my-payouts")]
    [ProducesResponseType(typeof(ApiResponse<List<MyPayoutDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPayouts()
    {
        var result = await Mediator.Send(new GetMyPayoutsQuery());
        return Ok(result);
    }
}
