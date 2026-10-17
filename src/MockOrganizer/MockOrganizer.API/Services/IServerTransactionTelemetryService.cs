using MockOrganizer.API.Models;

namespace MockOrganizer.API.Services;

public interface IServerTransactionTelemetryService
{
    /// <summary>
    /// Đo đạc time_to_checkout qua server timestamp, tra cứu concurrent_hold từ DB,
    /// và đo lường tần suất burst_rate_10s cùng device_account_overlap qua Redis Sliding Window (BE-PIPE-8.2.1 + BE-PIPE-8.2.2).
    /// </summary>
    Task<ServerTransactionTelemetryResult> MeasureAsync(
        string? sessionToken,
        string? buyerEmail,
        string? deviceFingerprint = null,
        string? clientIp = null,
        CancellationToken ct = default);
}
