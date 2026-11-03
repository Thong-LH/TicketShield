using System.Diagnostics;
using Microsoft.Extensions.Options;
using MockOrganizer.API.Controllers;
using MockOrganizer.API.Models;
using MockOrganizer.API.Services;

namespace MockOrganizer.API.AntiBotScoring;

/// <summary>
/// Dịch vụ bảo vệ Anti-Bot trước khi xử lý tồn kho (BE-PIPE-8.2.3 - SCRUM-253).
/// Tích hợp bộ đếm Redis, Client Telemetry SDK, mô hình ONNX Runtime In-Process,
/// và cơ chế Fail-Open tự động bảo vệ giao dịch trong SLA 20ms.
/// </summary>
public class AntiBotGuardService(
    IServerTransactionTelemetryService telemetryService,
    IAntiBotScoringService scoringService,
    ILogger<AntiBotGuardService> logger,
    IOptions<AntiBotScoringOptions>? options = null,
    TimeProvider? clock = null,
    IAntiBotAnalyticsCollector? analyticsCollector = null)
    : IAntiBotGuardService
{
    private readonly TimeSpan _guardTimeout = TimeSpan.FromMilliseconds(options?.Value.GuardTimeoutMs ?? 20);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<AntiBotGuardResult> EvaluateCheckoutGuardAsync(
        CheckoutRequest request,
        string? clientIp,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            // Thiết lập Guard Timeout (mặc định 20ms theo SLA)
            using var timeoutCts = new CancellationTokenSource(_guardTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var evaluateTask = ExecuteInternalAsync(request, clientIp, linkedCts.Token);
            var completedTask = await Task.WhenAny(evaluateTask, Task.Delay(_guardTimeout, ct));

            AntiBotGuardResult result;
            if (completedTask != evaluateTask)
            {
                sw.Stop();
                logger.LogWarning(
                    "AntiBotGuard: {TimeoutMs}ms SLA timeout exceeded ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                    _guardTimeout.TotalMilliseconds, sw.Elapsed.TotalMilliseconds);

                result = CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"SLA Timeout (> {_guardTimeout.TotalMilliseconds}ms)");
            }
            else
            {
                result = await evaluateTask;
            }

            RecordAnalytics(result, request);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogWarning(
                "AntiBotGuard: Operation timed out at 20ms ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                sw.Elapsed.TotalMilliseconds);

            var failResult = CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, "SLA Timeout (> 20ms)");
            RecordAnalytics(failResult, request);
            return failResult;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogWarning(ex,
                "AntiBotGuard: Error during evaluation ({ElapsedMs:F2}ms). Triggering Fail-Open fallback to ALLOW.",
                sw.Elapsed.TotalMilliseconds);

            var failResult = CreateFailOpenResult(sw.Elapsed.TotalMilliseconds, $"Exception: {ex.Message}");
            RecordAnalytics(failResult, request);
            return failResult;
        }
    }

    private void RecordAnalytics(AntiBotGuardResult result, CheckoutRequest request)
    {
        analyticsCollector?.RecordEvaluation(new AntiBotEvaluationLog
        {
            RequestId = request.SessionToken,
            BuyerEmail = request.BuyerEmail ?? string.Empty,
            Decision = result.Decision,
            RiskScore = result.RiskScore,
            LatencyMs = result.ExecutionDurationMs,
            IsFailOpen = result.IsFailOpen,
            Reason = result.Reason
        });
    }

    private async Task<AntiBotGuardResult> ExecuteInternalAsync(
        CheckoutRequest request,
        string? clientIp,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // 1. Thu thập telemetry Server (time_to_checkout, concurrent_hold, burst_rate_10s, device_account_overlap)
        var deviceFingerprint = request.ResolvedDeviceFingerprint;
        var telemetry = await telemetryService.MeasureAsync(
            request.SessionToken,
            request.BuyerEmail,
            deviceFingerprint,
            clientIp,
            ct);

        // 2. Lắp ráp 6D Feature Vector chuẩn hóa theo Hợp đồng BE-PIPE-8.2.3 và Spec 08:
        // [0] time_to_checkout (giây, Server Telemetry, -1.0 nếu token không hợp lệ)
        var timeToCheckout = (float)telemetry.TimeToCheckoutSeconds;

        // [1] action_interval_std_dev (ms, Client SDK, gán -1.0 nếu thiếu hoặc < 3 thao tác)
        var actionInterval = request.Telemetry?.ActionIntervalStdDev ?? -1.0f;

        // [2] burst_rate_10s (số request, Server Redis)
        var burstRate = (float)telemetry.BurstRate10s;

        // [3] concurrent_hold (số vé giữ, Server Redis - không query DB)
        var concurrentHold = (float)telemetry.ConcurrentHoldCount;

        // [4] is_webdriver (0.0 hoặc 1.0, Client SDK, gán -1.0 nếu client không gửi)
        float isWebdriver = request.Telemetry?.IsWebdriver.HasValue == true
            ? (request.Telemetry.IsWebdriver.Value > 0 ? 1.0f : 0.0f)
            : -1.0f;

        // [5] device_account_overlap (số tài khoản, Server Redis, gán -1.0 nếu thiếu device_fingerprint)
        float deviceOverlap = telemetry.DeviceAccountOverlap >= 0
            ? (float)telemetry.DeviceAccountOverlap
            : -1.0f;

        var vector = new BotRiskVector
        {
            TimeToCheckout = timeToCheckout,
            ActionIntervalStdDev = actionInterval,
            BurstRate10s = burstRate,
            ConcurrentHold = concurrentHold,
            IsWebDriver = isWebdriver,
            DeviceAccountOverlap = deviceOverlap
        };

        // 3. Suy luận rủi ro Bot qua ONNX In-Process Runtime
        var scoreResponse = scoringService.Evaluate(vector);

        sw.Stop();
        var durationMs = sw.Elapsed.TotalMilliseconds;

        Enum.TryParse<AntiBotDecision>(scoreResponse.Decision, ignoreCase: true, out var decision);

        return new AntiBotGuardResult
        {
            Decision = decision,
            RiskScore = scoreResponse.RiskScore,
            IsFailOpen = false,
            ExecutionDurationMs = Math.Round(durationMs, 3),
            Vector = vector,
            Telemetry = telemetry,
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
            Telemetry = null,
            Reason = $"Fail-Open: {reason}"
        };
}
