using System.Diagnostics;
using MockOrganizer.API.Models;

namespace MockOrganizer.API.Services;

public class ServerTransactionTelemetryService(
    IPipelineHoldTracker holdTracker,
    ISessionTokenService sessionTokenService,
    TimeProvider? clock = null,
    ILogger<ServerTransactionTelemetryService>? logger = null)
    : IServerTransactionTelemetryService
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ServerTransactionTelemetryResult> MeasureAsync(
        string? sessionToken,
        string? buyerEmail,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var now = _clock.GetUtcNow();

        // 1. Đo time_to_checkout bằng Server Timestamp từ Session Token
        var (isValid, issuedAt, _) = sessionTokenService.ValidateSessionToken(sessionToken);
        double timeToCheckout;

        if (isValid)
        {
            var deltaSeconds = (now - issuedAt).TotalSeconds;
            // Đảm bảo tối thiểu 0.01s để tránh giá trị 0 do độ phân giải đồng hồ
            timeToCheckout = Math.Max(0.01, Math.Round(deltaSeconds, 4));
        }
        else
        {
            // Missing hoặc Invalid session token -> -1.0 theo Spec 08
            timeToCheckout = -1.0;
        }

        // 2. Tra cứu số vé đang giữ đồng thời (concurrent_hold) từ Pipeline Cache (key hold:{buyerEmail})
        var concurrentHold = 0;
        if (!string.IsNullOrWhiteSpace(buyerEmail))
        {
            try
            {
                concurrentHold = await holdTracker.GetHoldCountAsync(buyerEmail, ct);
            }
            catch (Exception ex)
            {
                // Cơ chế Fail-Open: Không làm gián đoạn luồng mua nếu cache lỗi
                logger?.LogWarning(ex, "Failed to query concurrent hold from pipeline cache for {Email}. Defaulting to 0.", buyerEmail);
                concurrentHold = 0;
            }
        }

        sw.Stop();
        var executionDurationMs = sw.Elapsed.TotalMilliseconds;

        return new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: timeToCheckout,
            ConcurrentHoldCount: concurrentHold,
            IssuedAtUtc: isValid ? issuedAt : DateTimeOffset.MinValue,
            ReceivedAtUtc: now,
            ExecutionDurationMs: executionDurationMs,
            IsSessionValid: isValid
        );
    }
}
