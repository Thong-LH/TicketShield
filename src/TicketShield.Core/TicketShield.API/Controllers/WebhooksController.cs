using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

namespace TicketShield.API.Controllers;

[Route("api/webhooks")]
[AllowAnonymous]
public class WebhooksController : ApiControllerBase
{
    private readonly VietQrSettings _vietQrSettings;

    public WebhooksController(IOptions<VietQrSettings> vietQrOptions)
    {
        _vietQrSettings = vietQrOptions.Value;
    }

    /// <summary>
    /// SCRUM-80 / US-3.2: Receive SePay VietQR payment webhook and lock escrow transaction
    /// </summary>
    [HttpPost("sepay")]
    [ProducesResponseType(typeof(ApiResponse<ProcessSePayWebhookResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleSePayWebhook([FromBody] SePayWebhookRequest payload)
    {
        // 1. Authenticate Authorization Header against VietQR:WebhookSecret
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            return Unauthorized(ApiResponse<object>.FailureResponse("Truy cập bị từ chối: Thiếu header Authorization."));
        }

        var token = authHeader.Trim();
        if (token.StartsWith("Apikey ", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Apikey ".Length..].Trim();
        }
        else if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token["Bearer ".Length..].Trim();
        }

        if (!string.Equals(token, _vietQrSettings.WebhookSecret, StringComparison.Ordinal))
        {
            return Unauthorized(ApiResponse<object>.FailureResponse("Truy cập bị từ chối: Secret Webhook không hợp lệ."));
        }

        // 2. Process Webhook Command
        var result = await Mediator.Send(new ProcessSePayWebhookCommand(payload));
        return Ok(result);
    }
}
