using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Commands.UpdateEscrowBuffer;
using TicketShield.Application.Features.Admin.EscrowBuffer.Models;
using TicketShield.Application.Features.Admin.EscrowBuffer.Queries.GetEscrowBuffer;

namespace TicketShield.API.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("api/v1/admin/escrow-buffer")]
public class EscrowBufferController : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<EscrowBufferConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetEscrowBuffer()
    {
        var result = await Mediator.Send(new GetEscrowBufferQuery());
        return Ok(result);
    }

    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<EscrowBufferConfig>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateEscrowBuffer([FromBody] UpdateEscrowBufferCommand command)
    {
        var result = await Mediator.Send(command);
        return Ok(result);
    }
}
