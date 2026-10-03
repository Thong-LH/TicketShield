using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessTicketUsedWebhook;

namespace TicketShield.API.Controllers;

[Route("api/v1/webhooks/organizer")]
[AllowAnonymous]
public class OrganizerWebhooksController : ApiControllerBase
{
    private readonly ILogger<OrganizerWebhooksController> _logger;

    public OrganizerWebhooksController(ILogger<OrganizerWebhooksController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// SCRUM-170 / BE-CORE-5.2.4c: Receive Organizer webhook when a ticket is used to trigger early payout
    /// </summary>
    [HttpPost("ticket-used")]
    [ProducesResponseType(typeof(ApiResponse<ProcessTicketUsedWebhookResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HandleTicketUsedWebhook([FromBody] OrganizerTicketUsedWebhookRequest payload)
    {
        var result = await Mediator.Send(new ProcessTicketUsedWebhookCommand(payload));
        return Ok(result);
    }
}
