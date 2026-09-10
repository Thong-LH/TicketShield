using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.API.Controllers;

[ApiController]
[Authorize]
[Route("api/ticket-verifications")]
[Route("api/v1/ticket-verifications")]
[ResaleErrors]
public sealed class ResaleController(IServiceProvider services) : ControllerBase
{
    private ITicketVerificationService VerificationService =>
        services.GetService<ITicketVerificationService>()
        ?? throw new ResaleWorkflowException("RESALE_NOT_CONFIGURED", 503);

    private string Seller => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? throw new ResaleWorkflowException("MISSING_SUBJECT", 401);

    private IActionResult WorkflowResponse(VerificationResult result) =>
        StatusCode(result.Status.EndsWith("Pending", StringComparison.Ordinal) ? 202 : 200,
            ApiResponse<VerificationResult>.SuccessResponse(result, result.Status));

    [HttpPost]
    public async Task<IActionResult> RequestOtp(
        [FromBody] RequestOtpBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Request(Seller, key, body.TicketCode, ct));

    [HttpPost("{id}/resend")]
    public async Task<IActionResult> Resend(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Resend(Seller, id, key, ct));

    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(
        string id,
        [FromBody] ConfirmOtpBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Confirm(Seller, id, key, body.Otp, ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Get(Seller, id, ct));

    [HttpPost("{id}/close")]
    public async Task<IActionResult> Close(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Close(Seller, id, key, ct));

    [HttpPost("/api/resale-listings")]
    public async Task<IActionResult> Publish(
        [FromBody] PublishBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Publish(Seller, key, body, ct));

    [HttpPost("{id}/cancel-listing")]
    public async Task<IActionResult> Cancel(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await VerificationService.Cancel(Seller, id, key, ct));

    [AllowAnonymous]
    [HttpGet("/api/resale-listings")]
    [HttpGet("/api/v1/resale-listings")]
    public async Task<IActionResult> Marketplace(CancellationToken ct, int page = 1, int size = 20) =>
        Ok(ApiResponse<List<ListingResult>>.SuccessResponse(await VerificationService.Marketplace(page, size, ct)));
}

public sealed class ResaleErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not ResaleWorkflowException error) return;
        context.Result = new ObjectResult(ApiResponse<object>.FailureResponse(error.Code, [error.Code]))
        {
            StatusCode = error.HttpStatus
        };
        context.ExceptionHandled = true;
    }
}
