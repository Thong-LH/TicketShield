using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Models;

namespace MockOrganizer.API.Services;

public class ServerTransactionTelemetryService(
    OrganizerDbContext? db = null,
    ISessionTokenService sessionTokenService = null!,
    IRedisSlidingWindowService? slidingWindowService = null,
    TimeProvider? clock = null,
    ILogger<ServerTransactionTelemetryService>? logger = null,
    IServiceScopeFactory? scopeFactory = null)
    : IServerTransactionTelemetryService
{
    private static readonly TimeSpan DefaultHoldWindow = TimeSpan.FromMinutes(10);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly IRedisSlidingWindowService _slidingWindow =
        slidingWindowService ?? new RedisSlidingWindowService(null, null, clock);

    public async Task<ServerTransactionTelemetryResult> MeasureAsync(
        string? sessionToken,
        string? buyerEmail,
        string? deviceFingerprint = null,
        string? clientIp = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var now = _clock.GetUtcNow();

        // 1. Đo time_to_checkout bằng Server Timestamp bảo toàn SessionStart từ Session Token (US-14.1)
        var validationResult = sessionTokenService.ValidateSessionTokenV2(sessionToken);
        double timeToCheckout;

        if (validationResult.IsValid)
        {
            var sessionStart = validationResult.SessionStart != DateTimeOffset.MinValue
                ? validationResult.SessionStart
                : validationResult.IssuedAt;
            var deltaSeconds = (now - sessionStart).TotalSeconds;
            // Đảm bảo tối thiểu 0.01s để tránh giá trị 0 do độ phân giải đồng hồ
            timeToCheckout = Math.Max(0.01, Math.Round(deltaSeconds, 4));
        }
        else
        {
            // Missing hoặc Invalid session token -> -1.0 theo Spec 08
            timeToCheckout = -1.0;
        }

        // 2 & 3. Pipeline đồng thời tra cứu concurrent_hold và sliding window telemetry (burst_rate_10s, device_account_overlap)
        var holdTask = _slidingWindow.GetConcurrentHoldAsync(buyerEmail, ct);
        var redisTelemetryTask = _slidingWindow.MeasureSlidingWindowTelemetryAsync(clientIp, deviceFingerprint, buyerEmail, ct);

        await Task.WhenAll(holdTask, redisTelemetryTask);

        var concurrentHold = await holdTask;
        var redisTelemetry = await redisTelemetryTask;

        // Fallback DB nếu Redis chưa có bản ghi (ví dụ test legacy seed DB trực tiếp).
        // Luôn tạo scope riêng khi có scopeFactory để tránh dùng chung OrganizerDbContext
        // với luồng Checkout khi AntiBotGuard vượt timeout 20ms và chạy ngầm.
        if (concurrentHold == 0 && !string.IsNullOrWhiteSpace(buyerEmail) && (scopeFactory != null || db != null))
        {
            try
            {
                var cutoff = now - DefaultHoldWindow;
                if (scopeFactory != null)
                {
                    using var scope = scopeFactory.CreateScope();
                    var scopedDb = scope.ServiceProvider.GetRequiredService<OrganizerDbContext>();
                    concurrentHold = await QueryPendingHoldCountAsync(scopedDb, buyerEmail, cutoff, ct);
                }
                else if (db != null)
                {
                    concurrentHold = await QueryPendingHoldCountAsync(db, buyerEmail, cutoff, ct);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to query fallback DB hold for {Email}.", buyerEmail);
            }
        }

        sw.Stop();
        var executionDurationMs = sw.Elapsed.TotalMilliseconds;

        return new ServerTransactionTelemetryResult(
            TimeToCheckoutSeconds: timeToCheckout,
            ConcurrentHoldCount: concurrentHold,
            IssuedAtUtc: validationResult.IsValid ? validationResult.IssuedAt : DateTimeOffset.MinValue,
            ReceivedAtUtc: now,
            ExecutionDurationMs: executionDurationMs,
            IsSessionValid: validationResult.IsValid,
            BurstRate10s: redisTelemetry.BurstRate10s,
            DeviceAccountOverlap: redisTelemetry.DeviceAccountOverlap,
            RedisDurationMs: redisTelemetry.ExecutionDurationMs
        );
    }

    private static async Task<int> QueryPendingHoldCountAsync(
        OrganizerDbContext context,
        string buyerEmail,
        DateTimeOffset cutoff,
        CancellationToken ct)
    {
        return await context.MockOrders
            .AsNoTracking()
            .Where(o => o.BuyerEmail == buyerEmail
                     && o.Status == "PENDING"
                     && o.CreatedAt >= cutoff)
            .SumAsync(o => (int?)o.Quantity, ct) ?? 0;
    }
}
