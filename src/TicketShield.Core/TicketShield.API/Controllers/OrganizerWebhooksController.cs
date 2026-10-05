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
    private readonly IConfiguration _configuration;

    public OrganizerWebhooksController(
        ILogger<OrganizerWebhooksController> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// SCRUM-170 / BE-CORE-5.2.4c: Receive Organizer webhook when a ticket is used to trigger early payout
    /// </summary>
    [HttpPost("ticket-used")]
    [ProducesResponseType(typeof(ApiResponse<ProcessTicketUsedWebhookResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> HandleTicketUsedWebhook([FromBody] OrganizerTicketUsedWebhookRequest payload)
    {
        // FIX Lỗi 07: Xác thực Secret Key của Ban tổ chức gửi webhook
        var secret = _configuration["OrganizerWebhook:Secret"]
                     ?? _configuration["OrganizerGrpc:ApiKey"]
                     ?? "TicketShieldDevelopmentApiKeyForCapstone2026!";

        var providedSecret = Request.Headers["X-Organizer-Secret"].FirstOrDefault()
            ?? Request.Headers["X-Organizer-ApiKey"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedSecret))
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(authHeader))
            {
                var token = authHeader.Trim();
                if (token.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase)) token = token["Apikey ".Length..].Trim();
                else if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token["Bearer ".Length..].Trim();
                providedSecret = token;
            }
        }

        if (string.IsNullOrWhiteSpace(providedSecret) || !string.Equals(providedSecret, secret, StringComparison.Ordinal))
        {
            _logger.LogWarning("Organizer webhook unauthorized. Missing or invalid secret.");
            return Unauthorized(ApiResponse<object>.FailureResponse("Truy cập bị từ chối: Secret Webhook Ban tổ chức không hợp lệ."));
        }

        var result = await Mediator.Send(new ProcessTicketUsedWebhookCommand(payload));
        return Ok(result);
    }
}
