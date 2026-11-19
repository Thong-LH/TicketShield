using System.Text.Json;
using System.Text.Json.Serialization;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;
using TicketShield.AntiBot.Gateway.Services.Network;

namespace TicketShield.AntiBot.Gateway.Middleware;

/// <summary>
/// DTO tối giản để trích xuất tín hiệu telemetry từ request body của checkout.
/// </summary>
public class CheckoutTelemetryPayload
{
    [JsonPropertyName("sessionToken")]
    public string? SessionToken { get; set; }

    [JsonPropertyName("buyerEmail")]
    public string? BuyerEmail { get; set; }

    [JsonPropertyName("tierId")]
    public Guid? TierId { get; set; }

    [JsonPropertyName("deviceFingerprint")]
    public string? DeviceFingerprint { get; set; }

    [JsonPropertyName("telemetry")]
    public ClientTelemetryDto? Telemetry { get; set; }
}

public class ClientTelemetryDto
{
    [JsonPropertyName("actionIntervalStdDev")]
    public float? ActionIntervalStdDev { get; set; }

    [JsonPropertyName("isWebdriver")]
    public float? IsWebdriver { get; set; }

    [JsonPropertyName("virtualGpuDetected")]
    public float? VirtualGpuDetected { get; set; }

    [JsonPropertyName("prototypeTampered")]
    public float? PrototypeTampered { get; set; }

    [JsonPropertyName("canvasHash")]
    public string? CanvasHash { get; set; }

    [JsonPropertyName("canvasTampered")]
    public float? CanvasTampered { get; set; }

    [JsonPropertyName("webglRenderer")]
    public string? WebglRenderer { get; set; }

    [JsonPropertyName("deviceFingerprint")]
    public string? DeviceFingerprint { get; set; }

    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }

    [JsonPropertyName("client_timestamp")]
    public long? ClientTimestampSnake { get; set; }

    [JsonPropertyName("clientTimestamp")]
    public long? ClientTimestampCamel { get; set; }

    public long? ClientTimestamp => ClientTimestampSnake ?? ClientTimestampCamel;
}

/// <summary>
/// YARP Gateway Interceptor Middleware:
/// Đứng ở vòng ngoài cùng (Perimeter) chặn đứng bot traffic TRƯỚC KHI chạm tới hạ tầng Ban tổ chức.
/// </summary>
public class AntiBotCheckoutGuardMiddleware(
    RequestDelegate next,
    ILogger<AntiBotCheckoutGuardMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task InvokeAsync(
        HttpContext context,
        IAntiBotGuardService guardService,
        INetworkConsistencyService networkService,
        ISessionTokenService tokenService,
        INonceValidationService nonceService)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        // Chỉ chặn luồng checkout mua vé sơ cấp
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/organizer/orders/checkout", StringComparison.OrdinalIgnoreCase))
        {
            // 1. Đánh giá tính nhất quán mạng toàn diện (NET-13.1: TLS, HTTP/2, Client Hints, Subnet /24)
            var networkSummary = await networkService.EvaluateAsync(context, context.RequestAborted);
            context.Items["NetworkConsistencySummary"] = networkSummary;
            context.Items["TlsProfile"] = networkSummary.Tls;
            context.Items["Http2Profile"] = networkSummary.Http2;
            context.Items["ClientHintsProfile"] = networkSummary.ClientHints;
            context.Items["SubnetRateMetrics"] = networkSummary.Subnet;

            if (networkSummary.HasInconsistency)
            {
                logger.LogWarning(
                    "AntiBot Gateway: Flagged Network Inconsistency! Score={Score}, Reason={Reason}",
                    networkSummary.OverallInconsistencyScore, networkSummary.PrimaryReason);

                context.Request.Headers["X-Network-Inconsistency-Score"] = "1";
                context.Request.Headers["X-Network-Inconsistency-Reason"] = networkSummary.PrimaryReason ?? "Network Inconsistency";
            }

            context.Request.EnableBuffering();

            string? sessionToken = null;
            string? buyerEmail = null;
            Guid? payloadTierId = null;
            string? deviceFingerprint = null;
            float? actionInterval = null;
            float? isWebdriver = null;
            string? clientNonce = null;
            long? clientTimestamp = null;

            try
            {
                using var reader = new StreamReader(
                    context.Request.Body,
                    leaveOpen: true);

                var bodyText = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Rewind ngay lập tức cho YARP forward

                if (!string.IsNullOrWhiteSpace(bodyText))
                {
                    using var doc = JsonDocument.Parse(bodyText);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("sessionToken", out var st) || root.TryGetProperty("SessionToken", out st))
                        sessionToken = st.GetString();

                    if (root.TryGetProperty("buyerEmail", out var be) || root.TryGetProperty("BuyerEmail", out be))
                        buyerEmail = be.GetString();

                    if (root.TryGetProperty("tierId", out var ti) || root.TryGetProperty("TierId", out ti))
                    {
                        if (ti.TryGetGuid(out var parsedGuid))
                            payloadTierId = parsedGuid;
                        else if (Guid.TryParse(ti.GetString(), out var strGuid))
                            payloadTierId = strGuid;
                    }

                    if (root.TryGetProperty("deviceFingerprint", out var df) || root.TryGetProperty("device_fingerprint", out df))
                        deviceFingerprint = df.GetString();

                    if (root.TryGetProperty("telemetry", out var te) && te.ValueKind == JsonValueKind.Object)
                    {
                        if (te.TryGetProperty("nonce", out var no) || te.TryGetProperty("Nonce", out no))
                            clientNonce = no.GetString();

                        if (te.TryGetProperty("client_timestamp", out var ct) || te.TryGetProperty("clientTimestamp", out ct))
                        {
                            if (ct.TryGetInt64(out var tsVal))
                                clientTimestamp = tsVal;
                        }

                        if (te.TryGetProperty("is_webdriver", out var iw) || te.TryGetProperty("isWebdriver", out iw))
                        {
                            if (iw.TryGetSingle(out var iwVal))
                                isWebdriver = iwVal;
                        }

                        if (te.TryGetProperty("action_interval_std_dev", out var ai) || te.TryGetProperty("actionIntervalStdDev", out ai))
                        {
                            if (ai.TryGetSingle(out var aiVal))
                                actionInterval = aiVal;
                        }

                        if (string.IsNullOrWhiteSpace(deviceFingerprint))
                        {
                            if (te.TryGetProperty("device_fingerprint", out var tdf) || te.TryGetProperty("deviceFingerprint", out tdf))
                                deviceFingerprint = tdf.GetString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to inspect checkout request body. Falling back to headers.");
                context.Request.Body.Position = 0;
            }

            // Fallback sang headers nếu body không có
            sessionToken ??= context.Request.Headers["X-Session-Token"].FirstOrDefault();
            deviceFingerprint ??= context.Request.Headers["X-Device-Fingerprint"].FirstOrDefault();
            clientNonce ??= context.Request.Headers["X-AntiBot-Nonce"].FirstOrDefault();

            // 2. Xác thực SessionToken v2 & Chống Replay Nonce (US-13.2.3, SCRUM-376)
            if (!string.IsNullOrWhiteSpace(sessionToken))
            {
                var tokenResult = tokenService.ValidateSessionTokenV2(sessionToken);
                if (!tokenResult.IsValid)
                {
                    logger.LogWarning("AntiBot Gateway: REJECTED invalid session token. Reason={Reason}", tokenResult.FailureReason);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = $"Invalid session token: {tokenResult.FailureReason}",
                        code = "InvalidSessionToken"
                    });
                    return;
                }

                // Kiểm tra khớp hạng vé nếu request có truyền tierId
                var requestTierId = payloadTierId ?? Guid.Empty;
                if (requestTierId != Guid.Empty && tokenResult.TierId != Guid.Empty && tokenResult.TierId != requestTierId)
                {
                    logger.LogWarning("AntiBot Gateway: REJECTED Tier Mismatch. Token Tier={TokenTier}, Request Tier={ReqTier}",
                        tokenResult.TierId, requestTierId);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "Session token was issued for a different ticket tier.",
                        code = "TierMismatch"
                    });
                    return;
                }

                // Nếu là Token v2 (có nhúng One-Time Nonce):
                if (tokenResult.TokenVersion == 2 && !string.IsNullOrWhiteSpace(tokenResult.Nonce))
                {
                    // a) Đối chiếu Telemetry Nonce vs Token Nonce
                    if (!string.Equals(clientNonce, tokenResult.Nonce, StringComparison.Ordinal))
                    {
                        logger.LogWarning("AntiBot Gateway: REJECTED Nonce Mismatch! Token Nonce={TokenNonce}, Telemetry Nonce={TelemetryNonce}",
                            tokenResult.Nonce, clientNonce);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = "Cryptographic Nonce mismatch between Session Token and Telemetry payload.",
                            code = "NonceMismatch"
                        });
                        return;
                    }

                    // b) Kiểm tra Clock Skew (dung sai ±15s)
                    if (clientTimestamp.HasValue)
                    {
                        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var skewMs = Math.Abs(nowMs - clientTimestamp.Value);
                        if (skewMs > 15_000)
                        {
                            logger.LogWarning("AntiBot Gateway: REJECTED Clock Skew anomaly. Skew={SkewMs}ms (Allowed: ±15000ms)", skewMs);
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsJsonAsync(new
                            {
                                message = "Client timestamp is outside the allowed window (±15s).",
                                code = "ClockSkew"
                            });
                            return;
                        }
                    }

                    // c) Tiêu thụ Nonce nguyên tử chống Replay (Atomic Check-and-Consume)
                    var consumption = await nonceService.ValidateAndConsumeNonceAsync(tokenResult.Nonce, tokenResult.TierId, context.RequestAborted);
                    if (consumption.IsReplay)
                    {
                        logger.LogWarning("AntiBot Gateway: REPLAY ATTACK BLOCKED! Nonce {Nonce} already consumed.", tokenResult.Nonce);
                        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = "This checkout request has already been submitted (Replay Attack Blocked).",
                            code = "ReplayBlocked",
                            decision = "Block"
                        });
                        return;
                    }

                    if (consumption.IsExpired)
                    {
                        logger.LogWarning("AntiBot Gateway: REJECTED Nonce Expired for {Nonce}.", tokenResult.Nonce);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = "One-time session nonce has expired or does not exist.",
                            code = "NonceExpired"
                        });
                        return;
                    }

                    if (!consumption.IsSuccess)
                    {
                        logger.LogWarning("AntiBot Gateway: REJECTED Nonce validation failed: {Msg}", consumption.Message);
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new
                        {
                            message = consumption.Message,
                            code = "NonceInvalid"
                        });
                        return;
                    }

                    // Nonce hợp lệ và đã tiêu thụ thành công -> Gắn header chuyển tiếp sang MockOrganizer
                    context.Request.Headers["X-AntiBot-Nonce-Verified"] = "True";
                    context.Request.Headers["X-AntiBot-Nonce"] = tokenResult.Nonce;
                }
            }

            // Fallback sang headers nếu body không có
            sessionToken ??= context.Request.Headers["X-Session-Token"].FirstOrDefault();
            deviceFingerprint ??= context.Request.Headers["X-Device-Fingerprint"].FirstOrDefault();

            var clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                           ?? context.Connection.RemoteIpAddress?.ToString();

            // Chấm điểm qua AntiBotGuardService với SLA 20ms & Fail-Open
            var guardResult = await guardService.EvaluateCheckoutGuardAsync(
                sessionToken,
                buyerEmail,
                clientIp,
                deviceFingerprint,
                actionInterval,
                isWebdriver,
                context.RequestAborted);

            // 1. Block -> Trả về HTTP 429 Too Many Requests ngay tại Gateway (Bảo vệ BTC)
            if (guardResult.Decision == AntiBotDecision.Block)
            {
                logger.LogWarning(
                    "AntiBot Gateway: BLOCKED checkout attack. Score={Score}, Latency={Latency}ms, Reason={Reason}",
                    guardResult.RiskScore, guardResult.ExecutionDurationMs, guardResult.Reason);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Suspicious automated activity detected. Checkout blocked by TicketShield Perimeter Defense.",
                    decision = "Block",
                    riskScore = guardResult.RiskScore,
                    latencyMs = guardResult.ExecutionDurationMs
                });
                return;
            }

            // 2. Challenge -> Trả về HTTP 403 Forbidden ngay tại Gateway
            if (guardResult.Decision == AntiBotDecision.Challenge)
            {
                logger.LogInformation(
                    "AntiBot Gateway: CHALLENGE required. Score={Score}, Latency={Latency}ms",
                    guardResult.RiskScore, guardResult.ExecutionDurationMs);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Verification challenge required.",
                    decision = "Challenge",
                    riskScore = guardResult.RiskScore,
                    challengeRequired = true,
                    challengeMode = "recaptcha",
                    latencyMs = guardResult.ExecutionDurationMs
                });
                return;
            }

            // 3. Allow -> Gắn header xác thực và chuyển tiếp trong suốt qua YARP sang MockOrganizer
            context.Request.Headers["X-AntiBot-Verified"] = "True";
            context.Request.Headers["X-AntiBot-Score"] = guardResult.RiskScore.ToString("F4");
        }

        await next(context);
    }
}
