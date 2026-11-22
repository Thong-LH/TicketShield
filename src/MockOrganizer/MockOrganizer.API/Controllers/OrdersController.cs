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
    TimeProvider? clock = null,
    IAntiBotPolicyManager? policyManager = null,
    TicketShield.AntiBot.Verification.ChallengeVerificationService? cartVerifier = null)
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
        var (token, nonce, issuedAt, expiresInSeconds) = sessionTokenService.GenerateSessionTokenWithNonce(tierId, now, 300);
        return Ok(new SessionTokenResponse(
            SessionToken: token,
            TierId: tierId,
            IssuedAtUtc: issuedAt,
            ExpiresInSeconds: expiresInSeconds,
            Nonce: nonce
        ));
    }

    /// <summary>
    /// POST /api/organizer/orders/checkout
    /// Lock trừ tồn kho nguyên tử và tạo đơn hàng sơ cấp.
    /// Kiểm soát bởi 20ms Fail-Open Anti-Bot Guard (BE-PIPE-8.2.3).
    /// </summary>
    [HttpPost("checkout/cart")]
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest request, CancellationToken ct)
    {
        if (request.Quantity <= 0)
            return BadRequest(new { message = "Quantity must be greater than 0." });

        var isCart = string.Equals(Request?.Path.Value?.TrimEnd('/'), "/api/organizer/orders/checkout/cart", StringComparison.OrdinalIgnoreCase);
        var lines = new List<CheckoutLine> { new(request.TierId, request.Quantity, request.SessionToken) };
        if (isCart)
        {
            if (request.AdditionalItems is not { Count: > 0 })
                return BadRequest(new { message = "Cart items are required." });
            lines.AddRange(request.AdditionalItems);
            if (lines.Any(line => line == null || line.TierId == Guid.Empty || line.Quantity <= 0)
                || lines.Select(line => line.TierId).Distinct().Count() != lines.Count
                || lines.Sum(line => (long)line.Quantity) > int.MaxValue)
                return BadRequest(new { message = "Invalid cart quantities or duplicate ticket tiers." });

            foreach (var line in lines)
            {
                var session = sessionTokenService.ValidateSessionTokenV2(line.SessionToken);
                if (!session.IsValid || session.TierId != line.TierId || string.IsNullOrEmpty(session.Nonce))
                    return Conflict(new { code = "InvalidSession", message = "Invalid checkout session." });
                if (line.TierId == request.TierId)
                {
                    if (request.Telemetry?.Nonce != session.Nonce)
                        return Conflict(new { code = "InvalidSession", message = "Invalid checkout nonce." });
                    continue;
                }
                var check = await replayGuardService.ValidateAsync(new CheckoutRequest {
                    TierId = line.TierId, SessionToken = line.SessionToken,
                    Telemetry = new ClientTelemetryDto { Nonce = session.Nonce, ClientTimestamp = request.Telemetry?.ClientTimestamp }
                }, ct);
                if (!check.IsAllowed) return Conflict(new { code = check.Code, message = check.Message });
            }
        }
        else if (request.AdditionalItems != null)
            return BadRequest(new { message = "Use cart checkout for multiple tiers." });

        // SCRUM-247 & US-13.2.3: Chặn replay (nonce, lệch giờ, khớp hạng vé) TRƯỚC Anti-Bot Guard.
        // Nếu Gateway đã kiểm tra và xác thực Nonce (X-AntiBot-Nonce-Verified: True), MockOrganizer tin tưởng kết quả để tránh Double-Claim
        var isNonceVerifiedByGateway = HttpContext?.Request.Headers.TryGetValue("X-AntiBot-Nonce-Verified", out var nonceVerified) == true
            && string.Equals(nonceVerified, "True", StringComparison.OrdinalIgnoreCase);

        if (!isNonceVerifiedByGateway)
        {
            var replayResult = await replayGuardService.ValidateAsync(request, ct);
            if (!replayResult.IsAllowed)
            {
                logger.LogWarning(
                    "Checkout REJECTED by ReplayGuard. Code={Code}, TierId={TierId}",
                    replayResult.Code, request.TierId);

                return Conflict(new { message = replayResult.Message, code = replayResult.Code });
            }
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

        // Tier 2: Challenge (0.35 <= RiskScore < 0.75) -> Yêu cầu CAPTCHA / Turnstile challenge
        var isChallengeVerifiedByGateway = HttpContext?.Request.Headers.TryGetValue("X-AntiBot-Challenge-Verified", out var chVerified) == true
            && string.Equals(chVerified, "True", StringComparison.OrdinalIgnoreCase);

        var isChallengeRequired = guardResult.Decision == AntiBotDecision.Challenge ||
            (isCart && HttpContext?.Request.Headers.ContainsKey("X-AntiBot-Cart-Challenge") == true);

        if (!string.IsNullOrWhiteSpace(request.ChallengeToken))
        {
            // Nếu chưa được Gateway xác thực (direct call vào MockOrganizer hoặc cart checkout), bắt buộc phải tự verify qua cartVerifier
            if (!isChallengeVerifiedByGateway)
            {
                var error = cartVerifier == null ? "ChallengeVerificationUnavailable"
                    : await cartVerifier.VerifyAsync(request.ChallengeToken, request.ChallengeProvider, ct);

                if (error != null)
                {
                    var unavailable = error == "ChallengeVerificationUnavailable";
                    return StatusCode(unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status403Forbidden,
                        new { code = error, message = isCart ? "Could not verify this cart. Please retry checkout." : (unavailable ? "Verification service unavailable. Please retry." : "Verification failed or expired. Please retry checkout.") });
                }

                isChallengeVerifiedByGateway = true;
            }

            logger.LogInformation(
                "Checkout Challenge token verified ({Provider}). Elevating decision to ALLOW for {Email}.",
                request.ChallengeProvider ?? "turnstile", request.BuyerEmail);
        }
        else if (isChallengeRequired)
        {
            logger.LogInformation(
                "Checkout CHALLENGED by AntiBotGuard. RiskScore={Score}. Triggering verification challenge.",
                guardResult.RiskScore);

            var preferred = policyManager?.GetPolicy().ChallengeMode;
            if (string.IsNullOrWhiteSpace(preferred))
            {
                preferred = "recaptcha";
            }

            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Verification challenge required to proceed with checkout.",
                decision = "Challenge",
                riskScore = guardResult.RiskScore,
                executionDurationMs = guardResult.ExecutionDurationMs,
                challengeRequired = true,
                preferredProvider = preferred
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
                var ids = lines.Select(line => line.TierId).ToArray();
                var tiers = await db.TicketTiers.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
                if (tiers.Count != lines.Count) return NotFound(new { message = "Ticket tier not found." });
                var firstTier = tiers[request.TierId];
                if (tiers.Values.Any(t => t.EventId != firstTier.EventId))
                    return BadRequest(new { message = "All cart items must belong to the same event." });
                foreach (var line in lines)
                {
                    var available = tiers[line.TierId].AvailableQuantity;
                    if (available < line.Quantity)
                        return Conflict(new { message = "Not enough inventory.", available, requested = line.Quantity });
                }

                var issuedTickets = new List<MockTicket>();
                var orders = new List<MockOrder>();
                var orgId = Guid.TryParse(resaleOptions?.OrganizerId, out var parsed) ? parsed
                    : Guid.Parse("e0000000-0000-0000-0000-000000000001");
                var mappings = new List<(string Code, TicketMapping Mapping)>();
                foreach (var line in lines)
                {
                    var tier = tiers[line.TierId];
                    // 3. Trừ tồn kho nguyên tử
                    tier.AvailableQuantity -= line.Quantity;

                    // 4. Tạo đơn hàng (Mua vé sơ cấp không cần thanh toán -> SUCCESS)
                    var order = new MockOrder
                    {
                        Id          = Guid.NewGuid(),
                        TierId      = tier.Id,
                        EventId     = tier.EventId,
                        TierName    = tier.TierName,
                        Quantity    = line.Quantity,
                        UnitPrice   = tier.Price,
                        TotalAmount = tier.Price * line.Quantity,
                        Status      = "SUCCESS",
                        BuyerEmail  = request.BuyerEmail,
                    };
                    db.MockOrders.Add(order);
                    orders.Add(order);

                    // 5. Sinh MockTicket chính thức cho từng vé trong cùng Transaction nguyên tử
                    for (var i = 0; i < line.Quantity; i++)
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
                            mappings.Add((ticketCode, new TicketMapping { EventId = tier.EventId, TierId = tier.Id.ToString() }));
                    }
                }

                // 6. SaveChanges — EF kiểm tra xmin, ném DbUpdateConcurrencyException nếu race
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                if (resaleOptions != null)
                    foreach (var mapping in mappings)
                        resaleOptions.TicketMappings[mapping.Code] = mapping.Mapping;
                var firstOrder = orders[0];
                var totalQuantity = orders.Sum(o => o.Quantity);

                // Ghi nhận số vé đang giữ vào Redis sau khi commit thành công (BE-PIPE-8.2.3)
                if (slidingWindowService != null && !string.IsNullOrWhiteSpace(request.BuyerEmail))
                {
                    await slidingWindowService.RecordHoldAsync(request.BuyerEmail, totalQuantity, ct: ct);
                }

                return Ok(new
                {
                    orderId            = firstOrder.Id,
                    tierId             = firstOrder.TierId,
                    eventId            = firstOrder.EventId,
                    tierName           = firstOrder.TierName,
                    quantity           = totalQuantity,
                    unitPrice          = firstOrder.UnitPrice,
                    totalAmount        = orders.Sum(o => o.TotalAmount),
                    status             = firstOrder.Status,
                    remainingAvailable = firstTier.AvailableQuantity,
                    tickets            = issuedTickets.Select(t => new
                    {
                        ticketCode  = t.TicketCode,
                        seatZone    = t.SeatZone,
                        originalPrice = t.OriginalPrice,
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

                db.ChangeTracker.Clear();
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

public record CheckoutLine(Guid TierId, int Quantity, string? SessionToken);

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
    public List<CheckoutLine>? AdditionalItems { get; set; }
    public string? ChallengeToken { get; set; }
    public string? ChallengeProvider { get; set; }

    public string? ResolvedDeviceFingerprint =>
        !string.IsNullOrWhiteSpace(DeviceFingerprint)
            ? DeviceFingerprint
            : Telemetry?.DeviceFingerprint;
}
