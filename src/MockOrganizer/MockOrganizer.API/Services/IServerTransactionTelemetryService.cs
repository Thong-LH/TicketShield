using MockOrganizer.API.Models;

namespace MockOrganizer.API.Services;

public interface IServerTransactionTelemetryService
{
    /// <summary>
    /// Đo đạc time_to_checkout qua server timestamp và tra cứu concurrent_hold từ DB (BE-PIPE-8.2.1).
    /// </summary>
    Task<ServerTransactionTelemetryResult> MeasureAsync(
        string? sessionToken,
        string? buyerEmail,
        CancellationToken ct = default);
}
