using System.Diagnostics;
using Microsoft.Extensions.Options;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

public interface IAntiBotGuardService
{
    Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        string? sessionToken,
        string? buyerEmail,
        string? clientIp,
        string? deviceFingerprint,
        float? actionIntervalStdDev,
        float? isWebdriver,
        CancellationToken ct = default);
}

public class AntiBotGuardService(
    ISessionTokenService sessionTokenService,
    IAntiBotRedisService redisService,
    IAntiBotScoringService scoringService,
    IOptions<AntiBotScoringOptions> options,
    ILogger<AntiBotGuardService> logger,
    TimeProvider? clock = null)
    : IAntiBotGuardService
{
    private readonly TimeSpan _guardTimeout = TimeSpan.FromMilliseconds(options.Value.GuardTimeoutMs);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        string? sessionToken,
        string? buyerEmail,
        string? clientIp,
        string? deviceFingerprint,
        float? actionIntervalStdDev,
        float? isWebdriver,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = new CancellationTokenSource(_guardTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var evaluateTask = ExecuteInternalAsync(
                sessionToken, buyerEmail, clientIp, deviceFingerprint, actionIntervalStdDev, isWebdriver, linkedCts.Token);

            var completedTask = await Task.WhenAny(evaluateTask, Task.Delay(_guardTimeout, ct));

            if (completedTask != evaluateTask)
            {
                sw.Stop();
                logger.LogWarning(
                    "AntiBot Gateway: {TimeoutMs}ms SLA timeout exceeded ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                    _guardTimeout.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);

                return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"SLA Timeout (> {_guardTimeout.TotalMilliseconds}ms)");
            }

            return await evaluateTask;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogWarning(
                "AntiBot Gateway: Operation timed out at {TimeoutMs}ms ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                _guardTimeout.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);

            return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, "SLA Timeout");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex,
                "AntiBot Gateway: Error during evaluation ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                sw.Elapsed.TotalMilliseconds);

            return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"Exception: {ex.Message}");
        }
    }

    private async Task<AntiBotGuardResult> ExecuteInternalAsync(
        string? sessionToken,
        string? buyerEmail,
        string? clientIp,
        string? deviceFingerprint,
        float? actionIntervalStdDev,
        float? isWebdriver,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var now = _clock.GetUtcNow();

        // 1. Đo time_to_checkout từ SessionToken
        var (isValid, issuedAt, _) = sessionTokenService.ValidateSessionToken(sessionToken);
        float timeToCheckout = isValid
            ? (float)Math.Max(0.01, Math.Round((now - issuedAt).TotalSeconds, 4))
            : -1.0f;

        // 2. Tra cứu Redis Sliding Window Telemetry
        var redisTelemetry = await redisService.MeasureTelemetryAsync(clientIp, deviceFingerprint, buyerEmail, ct);

        // 3. Lắp ráp 6D Feature Vector
        var vector = new BotRiskVector
        {
            TimeToCheckout = timeToCheckout,
            ActionIntervalStdDev = actionIntervalStdDev ?? -1.0f,
            BurstRate10s = redisTelemetry.BurstRate10s,
            ConcurrentHold = redisTelemetry.ConcurrentHold,
            IsWebDriver = isWebdriver ?? -1.0f,
            DeviceAccountOverlap = redisTelemetry.DeviceAccountOverlap
        };

        // 4. Chấm điểm rủi ro qua mô hình ONNX
        var scoreResponse = scoringService.Evaluate(vector);

        sw.Stop();
        Enum.TryParse<AntiBotDecision>(scoreResponse.Decision, ignoreCase: true, out var decision);

        return new AntiBotGuardResult
        {
            Decision = decision,
            RiskScore = scoreResponse.RiskScore,
            IsFailOpen = false,
            ExecutionDurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3),
            Vector = vector,
            Reason = scoreResponse.Details
        };
    }

    private static AntiBotGuardResult CreateFailOpenResult(double elapsedMs, string reason) =>
        new()
        {
            Decision = AntiBotDecision.Allow,
            RiskScore = 0.0f,
            IsFailOpen = true,
            ExecutionDurationMs = Math.Round(elapsedMs, 3),
            Vector = new BotRiskVector(),
            Reason = $"Fail-Open: {reason}"
        };
}
