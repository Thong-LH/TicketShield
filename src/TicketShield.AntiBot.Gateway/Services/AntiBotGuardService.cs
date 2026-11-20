using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TicketShield.AntiBot.Gateway.Models;

namespace TicketShield.AntiBot.Gateway.Services;

public interface IAntiBotGuardService
{
    Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        CheckoutTelemetryContext context,
        CancellationToken ct = default);

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
    IShadowMetricsAggregator? metricsAggregator = null,
    IFprMetricLogger? fprLogger = null,
    TimeProvider? clock = null)
    : IAntiBotGuardService
{
    private readonly TimeSpan _guardTimeout = TimeSpan.FromMilliseconds(options.Value.GuardTimeoutMs);
    private readonly AntiBotScoringOptions _options = options.Value;
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        string? sessionToken,
        string? buyerEmail,
        string? clientIp,
        string? deviceFingerprint,
        float? actionIntervalStdDev,
        float? isWebdriver,
        CancellationToken ct = default)
    {
        var context = new CheckoutTelemetryContext
        {
            SessionToken = sessionToken,
            BuyerEmail = buyerEmail,
            ClientIp = clientIp,
            DeviceFingerprint = deviceFingerprint,
            ActionIntervalStdDev = actionIntervalStdDev ?? -1.0f,
            IsWebDriver = isWebdriver ?? -1.0f
        };

        return EvaluateCheckoutGuardAsync(context, ct);
    }

    public async Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        CheckoutTelemetryContext context,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var timeoutCts = new CancellationTokenSource(_guardTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var evaluateTask = ExecuteInternalAsync(context, sw, linkedCts.Token);
            var completedTask = await Task.WhenAny(evaluateTask, Task.Delay(_guardTimeout, ct));

            if (completedTask != evaluateTask)
            {
                sw.Stop();
                logger.LogWarning(
                    "AntiBot Gateway: {TimeoutMs}ms SLA timeout exceeded ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                    _guardTimeout.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);

                return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"SLA Timeout (> {_guardTimeout.TotalMilliseconds}ms)", context);
            }

            return await evaluateTask;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogWarning(
                "AntiBot Gateway: Operation timed out at {TimeoutMs}ms ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                _guardTimeout.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);

            return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, "SLA Timeout", context);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex,
                "AntiBot Gateway: Error during evaluation ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                sw.Elapsed.TotalMilliseconds);

            return CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"Exception: {ex.Message}", context);
        }
    }

    private async Task<AntiBotGuardResult> ExecuteInternalAsync(
        CheckoutTelemetryContext context,
        Stopwatch sw,
        CancellationToken ct)
    {
        var now = _clock.GetUtcNow();

        // 1. Đo time_to_checkout từ SessionToken nếu chưa được trích xuất
        float timeToCheckout = context.TimeToCheckout;
        if (timeToCheckout <= 0 && !string.IsNullOrWhiteSpace(context.SessionToken))
        {
            var (isValid, issuedAt, _) = sessionTokenService.ValidateSessionToken(context.SessionToken);
            timeToCheckout = isValid
                ? (float)Math.Max(0.01, Math.Round((now - issuedAt).TotalSeconds, 4))
                : -1.0f;
        }

        // 2. Tra cứu Redis Sliding Window Telemetry
        var redisTelemetry = await redisService.MeasureTelemetryAsync(
            context.ClientIp, context.DeviceFingerprint, context.BuyerEmail, ct);

        // Bổ sung tín hiệu từ Redis nếu context chưa có số đo hành vi
        float burstRate = context.BurstRate10s > 1.0f ? context.BurstRate10s : redisTelemetry.BurstRate10s;
        float concurrentHold = context.ConcurrentHold > 0.0f ? context.ConcurrentHold : redisTelemetry.ConcurrentHold;
        float overlap = context.DeviceAccountOverlap >= 0.0f ? context.DeviceAccountOverlap : redisTelemetry.DeviceAccountOverlap;

        var enrichedContext = context with
        {
            TimeToCheckout = timeToCheckout,
            BurstRate10s = burstRate,
            ConcurrentHold = concurrentHold,
            DeviceAccountOverlap = overlap
        };

        // 3. Lắp ráp Vector 6 chiều (v1) và 11 chiều (v2)
        var vectorV1 = enrichedContext.ToRiskVectorV1();
        var vectorV2 = enrichedContext.ToRiskVectorV2();

        var mode = _options.ExecutionMode;

        // =========================================================================
        // CASE A: CHẾ ĐỘ ENFORCE (Mô hình v2.0 nắm quyền quyết định traffic)
        // =========================================================================
        if (mode == AntiBotExecutionMode.Enforce)
        {
            var v2Score = scoringService.EvaluateV2(vectorV2);
            AntiBotScoreResponse? v1Score = null;
            try
            {
                v1Score = scoringService.Evaluate(vectorV1);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AntiBot Gateway: Failed to evaluate v1 baseline during Enforce mode.");
            }

            string cohort = v1Score != null
                ? ComputeCohort(v1Score.Decision, v2Score.Decision)
                : "enforce_v2_only";

            double delta = v1Score != null
                ? Math.Round(Math.Abs(v1Score.RiskScore - v2Score.RiskScore), 4)
                : 0.0;
            bool matches = v1Score != null && string.Equals(v1Score.Decision, v2Score.Decision, StringComparison.OrdinalIgnoreCase);

            metricsAggregator?.RecordEvaluation(cohort, matches, delta, isError: false);

            if (fprLogger != null)
            {
                var logEntry = ShadowEvaluationLogEntry.Create(
                    requestId: enrichedContext.SessionToken ?? Guid.NewGuid().ToString("N"),
                    executionMode: AntiBotExecutionMode.Enforce,
                    context: enrichedContext,
                    primary: v2Score,
                    shadow: v1Score,
                    cohort: cohort,
                    evaluationMs: v2Score.InferenceTimeMs,
                    totalGuardMs: sw.Elapsed.TotalMilliseconds);
                fprLogger.LogEvaluation(logEntry);
            }

            sw.Stop();
            Enum.TryParse<AntiBotDecision>(v2Score.Decision, ignoreCase: true, out var decision);

            return new AntiBotGuardResult
            {
                Decision = decision,
                RiskScore = v2Score.RiskScore,
                IsFailOpen = false,
                ExecutionDurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3),
                Vector = vectorV1,
                VectorV2 = vectorV2,
                Reason = v2Score.Details,
                ExecutionMode = AntiBotExecutionMode.Enforce,
                ShadowCohort = cohort,
                ShadowRiskScore = v1Score?.RiskScore,
                ShadowDecision = v1Score?.Decision,
                TelemetryContext = enrichedContext
            };
        }

        // =========================================================================
        // CASE B: CHẾ ĐỘ SHADOW HOẶC AUDIT (Mô hình v1.0 quyết định traffic thực tế)
        // Mô hình v2.0 chạy ngầm với Fault Isolation tuyệt đối bảo vệ luồng checkout
        // =========================================================================
        var primaryScore = scoringService.Evaluate(vectorV1);

        AntiBotScoreResponse? shadowV2Score = null;
        string shadowCohort = "unknown";
        bool v2HasError = false;

        try
        {
            shadowV2Score = scoringService.EvaluateV2(vectorV2);
            shadowCohort = ComputeCohort(primaryScore.Decision, shadowV2Score.Decision);
        }
        catch (Exception ex)
        {
            v2HasError = true;
            logger.LogWarning(ex, "AntiBot Gateway: Fault Isolation intercepted Model v2.0 error in Shadow Mode. Primary checkout traffic is 100% safe.");
            shadowCohort = $"v1_{primaryScore.Decision.ToLowerInvariant()}_v2_error";
        }

        double scoreDelta = shadowV2Score != null
            ? Math.Round(Math.Abs(primaryScore.RiskScore - shadowV2Score.RiskScore), 4)
            : 0.0;
        bool decisionMatches = shadowV2Score != null &&
            string.Equals(primaryScore.Decision, shadowV2Score.Decision, StringComparison.OrdinalIgnoreCase);

        // Ghi nhận metrics in-memory tức thì (lock-free)
        metricsAggregator?.RecordEvaluation(shadowCohort, decisionMatches, scoreDelta, isError: v2HasError);

        // Ghi structured log phi đồng bộ ra file .jsonl (Non-blocking queue)
        if (fprLogger != null)
        {
            var logEntry = ShadowEvaluationLogEntry.Create(
                requestId: enrichedContext.SessionToken ?? Guid.NewGuid().ToString("N"),
                executionMode: mode,
                context: enrichedContext,
                primary: primaryScore,
                shadow: shadowV2Score,
                cohort: shadowCohort,
                evaluationMs: shadowV2Score?.InferenceTimeMs ?? 0.0,
                totalGuardMs: sw.Elapsed.TotalMilliseconds);
            fprLogger.LogEvaluation(logEntry);
        }

        sw.Stop();
        Enum.TryParse<AntiBotDecision>(primaryScore.Decision, ignoreCase: true, out var finalDecision);

        return new AntiBotGuardResult
        {
            Decision = finalDecision,
            RiskScore = primaryScore.RiskScore,
            IsFailOpen = false,
            ExecutionDurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3),
            Vector = vectorV1,
            VectorV2 = vectorV2,
            Reason = primaryScore.Details,
            ExecutionMode = mode,
            ShadowCohort = shadowCohort,
            ShadowRiskScore = shadowV2Score?.RiskScore,
            ShadowDecision = shadowV2Score?.Decision,
            TelemetryContext = enrichedContext
        };
    }

    private static string ComputeCohort(string dec1, string dec2)
    {
        string d1 = dec1.ToLowerInvariant();
        string d2 = dec2.ToLowerInvariant();
        return $"v1_{d1}_v2_{d2}";
    }

    private static AntiBotGuardResult CreateFailOpenResult(double elapsedMs, string reason, CheckoutTelemetryContext? context = null) =>
        new()
        {
            Decision = AntiBotDecision.Allow,
            RiskScore = 0.0f,
            IsFailOpen = true,
            ExecutionDurationMs = Math.Round(elapsedMs, 3),
            Vector = new BotRiskVector(),
            VectorV2 = new BotRiskVectorV2(),
            Reason = $"Fail-Open: {reason}",
            ExecutionMode = AntiBotExecutionMode.Shadow,
            ShadowCohort = "fail_open",
            TelemetryContext = context
        };
}
