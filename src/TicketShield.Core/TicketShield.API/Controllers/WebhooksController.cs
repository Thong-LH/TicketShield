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
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IOptions<VietQrSettings> vietQrOptions,
        ILogger<WebhooksController> logger)
    {
        _vietQrSettings = vietQrOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// SCRUM-80 / US-3.2: Receive SePay VietQR payment webhook and lock escrow transaction
    /// </summary>
    [HttpPost("sepay")]
    [HttpPost("/api/v1/webhooks/sepay")]
    [HttpPost("/api/v1/payments/sepay-webhook")]
    [ProducesResponseType(typeof(ApiResponse<ProcessSePayWebhookResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleSePayWebhook([FromBody] SePayWebhookRequest payload)
    {
        // 1. Authenticate Authorization Header against VietQR:WebhookSecret
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            _logger.LogWarning("SePay Webhook rejected: Missing Authorization header.");
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

        if (!string.Equals(token, _vietQrSettings?.WebhookSecret, StringComparison.Ordinal))
        {
            _logger.LogWarning("SePay Webhook rejected: Invalid Webhook Secret Token.");
            return Unauthorized(ApiResponse<object>.FailureResponse("Truy cập bị từ chối: Secret Webhook không hợp lệ."));
        }

        // 2. Process Webhook Command
        try
        {
            var result = await Mediator.Send(new ProcessSePayWebhookCommand(payload));
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xảy ra trong quá trình xử lý SePay Webhook.");
            return Ok(ApiResponse<object>.FailureResponse($"Lỗi xử lý Webhook: {ex.Message}"));
        }
    }
}
