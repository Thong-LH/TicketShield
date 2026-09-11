using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.API.Filters;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.API.Controllers;

/// <summary>
/// Controller điều phối quy trình xác thực vé qua gRPC với Nhà tổ chức (Ticket Verification &amp; Lock)
/// </summary>
[ApiController]
[Authorize]
[Route("api/ticket-verifications")]
[Route("api/v1/ticket-verifications")]
[ResaleErrors]
public class TicketVerificationsController(
    ITicketVerificationService verificationService,
    ICurrentUserService? currentUserService = null) : ControllerBase
{
    private string Seller => currentUserService?.UserId?.ToString("D")
                          ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                          ?? User.FindFirst("sub")?.Value
                          ?? throw new ResaleWorkflowException("MISSING_SUBJECT", 401);

    private IActionResult WorkflowResponse(VerificationResult result) =>
        StatusCode(result.Status.EndsWith("Pending", StringComparison.Ordinal) ? 202 : 200,
            ApiResponse<VerificationResult>.SuccessResponse(result, result.Status));

    /// <summary>
    /// Gửi yêu cầu xác thực vé: Nhà tổ chức gửi OTP qua email chủ vé
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RequestOtp(
        [FromBody] RequestOtpBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await verificationService.Request(Seller, key, body.TicketCode, ct));

    /// <summary>
    /// Gửi lại mã OTP xác thực
    /// </summary>
    [HttpPost("{id}/resend")]
    public async Task<IActionResult> Resend(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await verificationService.Resend(Seller, id, key, ct));

    /// <summary>
    /// Xác nhận mã OTP và yêu cầu Nhà tổ chức Khóa vé (Lock)
    /// </summary>
    [HttpPost("{id}/confirm")]
    public async Task<IActionResult> Confirm(
        string id,
        [FromBody] ConfirmOtpBody body,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await verificationService.Confirm(Seller, id, key, body.Otp, ct));

    /// <summary>
    /// Tra cứu trạng thái phiên xác thực vé
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct) =>
        WorkflowResponse(await verificationService.Get(Seller, id, ct));

    /// <summary>
    /// Đóng phiên xác thực vé trước khi đăng bán
    /// </summary>
    [HttpPost("{id}/close")]
    public async Task<IActionResult> Close(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await verificationService.Close(Seller, id, key, ct));

    /// <summary>
    /// Hủy tin đăng bán và yêu cầu Nhà tổ chức Mở khóa vé (Release Lock)
    /// </summary>
    [Obsolete("Sử dụng endpoint chuẩn POST /api/v1/resale-listings/{id}/cancel thay thế.")]
    [HttpPost("{id}/cancel-listing")]
    public async Task<IActionResult> Cancel(
        string id,
        [FromHeader(Name = "Idempotency-Key")] string key,
        CancellationToken ct) =>
        WorkflowResponse(await verificationService.Cancel(Seller, id, key, ct));
}

