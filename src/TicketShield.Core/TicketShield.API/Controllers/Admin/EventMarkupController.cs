using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Commands.UpdateEventResaleMarkup;
using TicketShield.Application.Features.Admin.EventMarkup.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Queries.GetEventResaleMarkup;

namespace TicketShield.API.Controllers.Admin;

[Authorize(Roles = "Admin")]
[Route("api/v1/admin/events")]
public class EventMarkupController : ApiControllerBase
{
    [HttpGet("{eventId:guid}/resale-markup")]
    [ProducesResponseType(typeof(ApiResponse<EventResaleMarkupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetResaleMarkup(Guid eventId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetEventResaleMarkupQuery(eventId), ct);
        return Ok(result);
    }

    [HttpPut("{eventId:guid}/resale-markup")]
    [ProducesResponseType(typeof(ApiResponse<EventResaleMarkupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateResaleMarkup(
        Guid eventId,
        [FromBody] UpdateEventResaleMarkupCommand command,
        CancellationToken ct)
    {
        command.EventId = eventId;
        var result = await Mediator.Send(command, ct);
        return Ok(result);
    }
}
