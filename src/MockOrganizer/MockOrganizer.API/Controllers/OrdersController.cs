using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.AntiBotScoring;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Models;
using MockOrganizer.API.Resale;
using MockOrganizer.API.Services;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/orders")]
public class OrdersController(
    OrganizerDbContext db,
    IServerTransactionTelemetryService telemetryService,
    ISessionTokenService sessionTokenService,
    IAntiBotGuardService antiBotGuardService,
    IReplayGuardService replayGuardService,
    ILogger<OrdersController> logger,
    IRedisSlidingWindowService? slidingWindowService = null,
    ResaleOptions? resaleOptions = null,
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
    /// Kiểm soát bởi 20ms Fail-Open Anti-Bot Guard (BE-PIPE-8.2.3).
    /// </summary>
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than 0." });

        // SCRUM-247: Chặn replay (nonce, lệch giờ, khớp hạng vé) TRƯỚC Anti-Bot Guard
        // để không bị Fail-Open bỏ qua và không làm tăng bộ đếm Redis Sliding Window.
        var replayResult = await replayGuardService.ValidateAsync(request, ct);
        if (!replayResult.IsAllowed)
        {
            logger.LogWarning(
                "Checkout REJECTED by ReplayGuard. Code={Code}, TierId={TierId}",
                replayResult.Code, request.TierId);

            return Conflict(new { message = replayResult.Message, code = replayResult.Code });
        }

        var clientIp = HttpContext?.Request?.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? HttpContext?.Connection?.RemoteIpAddress?.ToString();

        // BE-PIPE-8.2.3: Anti-Bot Guard check với SLA 20ms và cơ chế Fail-Open tự động TRƯỚC KHI xử lý DB
        var guardResult = await antiBotGuardService.EvaluateCheckoutGuardAsync(request, clientIp, ct);

        // Tier 3: Block (RiskScore >= 0.75) -> Chặn ngay lập tức, tuyệt đối không chọc DB
        if (guardResult.Decision == AntiBotDecision.Block)
        {
            logger.LogWarning(
                "Checkout BLOCKED by AntiBotGuard. RiskScore={Score}, TimeToCheckout={Time}s, Burst10s={Burst}, IsWebdriver={Wd}, DeviceOverlap={Overlap}",
                guardResult.RiskScore, guardResult.Vector.TimeToCheckout, guardResult.Vector.BurstRate10s, guardResult.Vector.IsWebDriver, guardResult.Vector.DeviceAccountOverlap);

            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = "Suspicious automated activity detected. Checkout blocked.",
                decision = "Block",
                riskScore = guardResult.RiskScore,
                executionDurationMs = guardResult.ExecutionDurationMs,
                reason = guardResult.Reason,
                telemetry = guardResult.Telemetry != null ? new
                {
                    timeToCheckoutSeconds = guardResult.Telemetry.TimeToCheckoutSeconds,
                    concurrentHoldCount   = guardResult.Telemetry.ConcurrentHoldCount,
                    burstRate10s          = guardResult.Telemetry.BurstRate10s,
                    deviceAccountOverlap  = guardResult.Telemetry.DeviceAccountOverlap,
                    executionDurationMs   = guardResult.Telemetry.ExecutionDurationMs,
                    redisDurationMs       = guardResult.Telemetry.RedisDurationMs,
                    isSessionValid        = guardResult.Telemetry.IsSessionValid
                } : null
            });
        }

        // Tier 2: Challenge (0.35 <= RiskScore < 0.75) -> Yêu cầu CAPTCHA / PoW challenge
        if (guardResult.Decision == AntiBotDecision.Challenge)
        {
            logger.LogInformation(
                "Checkout CHALLENGED by AntiBotGuard. RiskScore={Score}. Triggering verification challenge.",
                guardResult.RiskScore);

            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Verification challenge required to proceed with checkout.",
                decision = "Challenge",
                riskScore = guardResult.RiskScore,
                executionDurationMs = guardResult.ExecutionDurationMs,
                challengeRequired = true
            });
        }

        // Tier 1: Allow (RiskScore < 0.35) hoặc Fail-Open -> Tiến hành lock trừ tồn kho nguyên tử (xmin)
        var telemetry = guardResult.Telemetry ?? new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: guardResult.Vector.TimeToCheckout,
            ConcurrentHoldCount: (int)guardResult.Vector.ConcurrentHold,
            IssuedAtUtc: DateTimeOffset.MinValue,
            ReceivedAtUtc: _clock.GetUtcNow(),
            ExecutionDurationMs: guardResult.ExecutionDurationMs,
            IsSessionValid: guardResult.Vector.TimeToCheckout >= 0,
            BurstRate10s: (int)guardResult.Vector.BurstRate10s,
            DeviceAccountOverlap: (int)guardResult.Vector.DeviceAccountOverlap
        );

        logger.LogInformation(
            "Checkout allowed by AntiBotGuard (FailOpen={IsFailOpen}): RiskScore={Score}, GuardLatency={Duration}ms",
            guardResult.IsFailOpen, guardResult.RiskScore, guardResult.ExecutionDurationMs);

        var now = _clock.GetUtcNow();

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

                // 4. Tạo đơn hàng (Mua vé sơ cấp không cần thanh toán -> SUCCESS)
                var order = new MockOrder
                {
                    Id          = Guid.NewGuid(),
                    TierId      = tier.Id,
                    EventId     = tier.EventId,
                    TierName    = tier.TierName,
                    Quantity    = request.Quantity,
                    UnitPrice   = tier.Price,
                    TotalAmount = tier.Price * request.Quantity,
                    Status      = "SUCCESS",
                    BuyerEmail  = request.BuyerEmail,
                };
                db.MockOrders.Add(order);

                // 5. Sinh MockTicket chính thức cho từng vé trong cùng Transaction nguyên tử
                var orgId = Guid.TryParse(resaleOptions?.OrganizerId, out var g)
                    ? g
                    : Guid.Parse("e0000000-0000-0000-0000-000000000001");

                var issuedTickets = new List<MockTicket>();
                for (var i = 0; i < request.Quantity; i++)
                {
                    var randHex = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                    var ticketCode = $"{GenerateTierPrefix(tier.TierName)}-{now:yyyy}-{randHex}";
                    var ticket = new MockTicket
                    {
                        Id            = Guid.NewGuid(),
                        OrganizerId   = orgId,
                        TicketCode    = ticketCode,
                        EventName     = tier.EventId,
                        SeatZone      = tier.TierName,
                        OriginalPrice = tier.Price,
                        OwnerEmail    = request.BuyerEmail?.Trim() ?? string.Empty,
                        OwnerName     = request.BuyerName?.Trim(),
                        OwnerPhone    = request.BuyerPhone?.Trim(),
                        Status        = "VALID",
                        CreatedAt     = now.UtcDateTime,
                        UpdatedAt     = now.UtcDateTime,
                    };
                    db.MockTickets.Add(ticket);
                    issuedTickets.Add(ticket);

                    if (resaleOptions != null)
                    {
                        resaleOptions.TicketMappings[ticket.TicketCode] = new TicketMapping
                        {
                            EventId = tier.EventId,
                            TierId  = tier.Id.ToString()
                        };
                    }
                }

                // 6. SaveChanges — EF kiểm tra xmin, ném DbUpdateConcurrencyException nếu race
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                // Ghi nhận số vé đang giữ vào Redis sau khi commit thành công (BE-PIPE-8.2.3)
                if (slidingWindowService != null && !string.IsNullOrWhiteSpace(request.BuyerEmail))
                {
                    await slidingWindowService.RecordHoldAsync(request.BuyerEmail, request.Quantity, ct: ct);
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
                    tickets            = issuedTickets.Select(t => new
                    {
                        ticketCode  = t.TicketCode,
                        seatZone    = t.SeatZone,
                        ownerEmail  = t.OwnerEmail,
                        ownerName   = t.OwnerName,
                        ownerPhone  = t.OwnerPhone,
                        status      = t.Status,
                    }),
                    antiBot = new
                    {
                        decision        = guardResult.Decision.ToString(),
                        riskScore       = guardResult.RiskScore,
                        isFailOpen      = guardResult.IsFailOpen,
                        guardDurationMs = guardResult.ExecutionDurationMs
                    },
                    telemetry          = new
                    {
                        timeToCheckoutSeconds = telemetry.TimeToCheckoutSeconds,
                        concurrentHoldCount   = telemetry.ConcurrentHoldCount,
                        burstRate10s          = telemetry.BurstRate10s,
                        deviceAccountOverlap  = telemetry.DeviceAccountOverlap,
                        executionDurationMs   = telemetry.ExecutionDurationMs,
                        redisDurationMs       = telemetry.RedisDurationMs,
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

    private static string GenerateTierPrefix(string tierName)
    {
        var lower = tierName.ToLowerInvariant();
        if (lower.Contains("vip") || lower.Contains("diamond")) return "ATSH-VIP";
        if (lower.Contains("ga") || lower.Contains("standing")) return "ATSH-GA";
        return "ATSH-STD";
    }
}

public class CheckoutRequest
{
    public Guid TierId { get; set; }
    public int Quantity { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerPhone { get; set; }
    public string? SessionToken { get; set; }
    public string? DeviceFingerprint { get; set; }
    public ClientTelemetryDto? Telemetry { get; set; }

    public string? ResolvedDeviceFingerprint =>
        !string.IsNullOrWhiteSpace(DeviceFingerprint)
            ? DeviceFingerprint
            : Telemetry?.DeviceFingerprint;
}
