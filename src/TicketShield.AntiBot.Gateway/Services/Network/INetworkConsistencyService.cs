using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ Facade hợp nhất toàn bộ các tầng phòng thủ mạng và giao thức (NET-13.1):
/// - TLS Handshake & JA4 Fingerprint (NET-13.1.1)
/// - HTTP/2 Settings & Pseudo-Headers Order (NET-13.1.2)
/// - W3C Client Hints (Sec-Ch-Ua) & Sec-Fetch-* (NET-13.1.3)
/// - Subnet Correlation Tracking /24 (NET-13.1.3)
/// </summary>
public interface INetworkConsistencyService
{
    /// <summary>
    /// Đánh giá toàn diện tính nhất quán mạng và giao thức của request gửi tới Gateway.
    /// </summary>
    Task<NetworkConsistencySummary> EvaluateAsync(
        HttpContext context,
        CancellationToken ct = default);
}
