using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/orders")]
public class OrdersController(
    OrganizerDbContext db,
    IServerTransactionTelemetryService telemetryService,
    ISessionTokenService sessionTokenService,
    IPipelineHoldTracker holdTracker,
    ILogger<OrdersController> logger,
    TimeProvider? clock = null)
    : ControllerBase
{
    private const int MaxRetries = 3;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// GET /api/organizer/orders/session?tierId={tierId}
    /// Cấp phát session token chứa timestamp server phục vụ đo time_to_checkout (BE-PIPE-8.2.1).
    /// </summary>
    [HttpGet("session")]
    public IActionResult CreateSession([FromQuery] Guid tierId)
    {
        if (tierId == Guid.Empty)
            return BadRequest(new { message = "TierId must not be empty." });

        var now = _clock.GetUtcNow();
        var token = sessionTokenService.GenerateSessionToken(tierId, now);
        return Ok(new SessionTokenResponse(
            SessionToken: token,
            TierId: tierId,
            IssuedAtUtc: now,
            ExpiresInSeconds: 7200
        ));
    }

    /// <summary>
    /// POST /api/organizer/orders/checkout
    /// Lock trừ tồn kho nguyên tử và tạo đơn hàng sơ cấp.
    /// Đo đạc time_to_checkout và concurrent_hold (BE-PIPE-8.2.1).
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than 0." });

        // BE-PIPE-8.2.1: Đo đạc time_to_checkout và tra cứu concurrent_hold từ DB
        var telemetry = await telemetryService.MeasureAsync(request.SessionToken, request.BuyerEmail, ct);
        logger.LogInformation(
            "Checkout telemetry measured: TimeToCheckout={TimeToCheckout}s, ConcurrentHold={Hold}, Latency={Duration}ms, ValidSession={IsValid}",
            telemetry.TimeToCheckoutSeconds, telemetry.ConcurrentHoldCount, telemetry.ExecutionDurationMs, telemetry.IsSessionValid);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1. Đọc tier (tracked để EF detect xmin thay đổi)
                var tier = await db.TicketTiers
                    .FirstOrDefaultAsync(t => t.Id == request.TierId, ct);

                if (tier is null)
                    return NotFound(new { message = $"Tier '{request.TierId}' not found." });

                // 2. Kiểm tra tồn kho
                if (tier.AvailableQuantity < request.Quantity)
                    return Conflict(new
                    {
                        message   = "Not enough inventory.",
                        available = tier.AvailableQuantity,
                        requested = request.Quantity
                    });

                // 3. Trừ tồn kho nguyên tử
                tier.AvailableQuantity -= request.Quantity;

                // 4. Tạo đơn hàng
                var order = new MockOrder
                {
                    Id          = Guid.NewGuid(),
                    TierId      = tier.Id,
                    EventId     = tier.EventId,
                    TierName    = tier.TierName,
                    Quantity    = request.Quantity,
                    UnitPrice   = tier.Price,
                    TotalAmount = tier.Price * request.Quantity,
                    Status      = "PENDING",
                    BuyerEmail  = request.BuyerEmail,
                };
                db.MockOrders.Add(order);

                // 5. SaveChanges — EF kiểm tra xmin, ném DbUpdateConcurrencyException nếu race
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                // 6. Cập nhật số vé đang giữ đồng thời vào cache pipeline (key hold:{buyerEmail}, TTL 10 phút)
                if (!string.IsNullOrWhiteSpace(order.BuyerEmail))
                {
                    try
                    {
                        await holdTracker.IncrementHoldCountAsync(order.BuyerEmail, order.Quantity, TimeSpan.FromMinutes(10), ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to increment hold count in pipeline cache for {Email}.", order.BuyerEmail);
                    }
                }

                return Ok(new
                {
                    orderId            = order.Id,
                    tierId             = order.TierId,
                    eventId            = order.EventId,
                    tierName           = order.TierName,
                    quantity           = order.Quantity,
                    unitPrice          = order.UnitPrice,
                    totalAmount        = order.TotalAmount,
                    status             = order.Status,
                    remainingAvailable = tier.AvailableQuantity,
                    telemetry          = new
                    {
                        timeToCheckoutSeconds = telemetry.TimeToCheckoutSeconds,
                        concurrentHoldCount   = telemetry.ConcurrentHoldCount,
                        executionDurationMs   = telemetry.ExecutionDurationMs,
                        isSessionValid        = telemetry.IsSessionValid
                    }
                });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogWarning(
                    "Checkout attempt {Attempt}/{Max} lost concurrency race for tier {TierId}. Retrying...",
                    attempt, MaxRetries, request.TierId);

                if (attempt == MaxRetries)
                {
                    logger.LogError(ex, "Checkout failed after {Max} retries for tier {TierId}.",
                        MaxRetries, request.TierId);
                    return StatusCode(503, new { message = "Server is busy, please retry." });
                }

                // Back-off nhỏ trước khi retry
                await Task.Delay(attempt * 50, ct);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                logger.LogError(ex, "Unexpected error during checkout for tier {TierId}.", request.TierId);
                throw;
            }
        }

        return StatusCode(503, new { message = "Checkout failed unexpectedly." });
    }
}

public class CheckoutRequest
{
    public Guid TierId { get; set; }
    public int Quantity { get; set; }
    public string? BuyerEmail { get; set; }
    public string? SessionToken { get; set; }
}
