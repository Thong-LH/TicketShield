using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Commands.SuspendUser;
using TicketShield.Identity.Application.Features.AdminUsers.Models;
using TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUser;

namespace TicketShield.Identity.API.Controllers;

[Authorize(Roles = "Admin")]
[Route("api/v1/admin/users")]
public class AdminUsersController : ApiControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetAdminUserQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/suspension")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Suspend(Guid id, CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new SuspendUserCommand(id), cancellationToken);
        return Ok(result);
    }
}
