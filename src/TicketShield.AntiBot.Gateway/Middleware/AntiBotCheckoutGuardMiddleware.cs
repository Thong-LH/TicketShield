using System.Text.Json;
using System.Text.Json.Serialization;
using TicketShield.AntiBot.Gateway.Models;
using TicketShield.AntiBot.Gateway.Services;

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

    [JsonPropertyName("deviceFingerprint")]
    public string? DeviceFingerprint { get; set; }
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

    public async Task InvokeAsync(HttpContext context, IAntiBotGuardService guardService)
    {
        var path = context.Request.Path;
        var method = context.Request.Method;

        // Chỉ chặn luồng checkout mua vé sơ cấp
        if (HttpMethods.IsPost(method) &&
            path.Equals("/api/organizer/orders/checkout", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.EnableBuffering();

            string? sessionToken = null;
            string? buyerEmail = null;
            string? deviceFingerprint = null;
            float? actionInterval = null;
            float? isWebdriver = null;

            try
            {
                using var reader = new StreamReader(
                    context.Request.Body,
                    leaveOpen: true);

                var bodyText = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Rewind ngay lập tức cho YARP forward

                if (!string.IsNullOrWhiteSpace(bodyText))
                {
                    var payload = JsonSerializer.Deserialize<CheckoutTelemetryPayload>(bodyText, JsonOptions);
                    if (payload != null)
                    {
                        sessionToken = payload.SessionToken;
                        buyerEmail = payload.BuyerEmail;
                        deviceFingerprint = payload.DeviceFingerprint ?? payload.Telemetry?.DeviceFingerprint;
                        actionInterval = payload.Telemetry?.ActionIntervalStdDev;
                        isWebdriver = payload.Telemetry?.IsWebdriver;
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
