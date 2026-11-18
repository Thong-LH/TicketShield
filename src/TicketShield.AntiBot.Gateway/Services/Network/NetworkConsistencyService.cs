using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Triển khai dịch vụ Facade phòng thủ mạng hợp nhất (NET-13.1).
/// </summary>
public class NetworkConsistencyService(
    ITlsProfileExtractor tlsExtractor,
    IHttp2ConsistencyService http2Service,
    IClientHintsValidator clientHintsValidator,
    ISubnetCorrelationTracker subnetTracker,
    ILogger<NetworkConsistencyService> logger)
    : INetworkConsistencyService
{
    public async Task<NetworkConsistencySummary> EvaluateAsync(
        HttpContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 1. Trích xuất hồ sơ TLS Profile (NET-13.1.1)
        var tlsProfile = tlsExtractor.Extract(context);

        // 2. Kiểm tra tính nhất quán HTTP/2 Settings & Pseudo-Headers (NET-13.1.2)
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();
        var http2Profile = http2Service.Inspect(context, userAgent);

        // 3. Đối soát Client Hints (Sec-Ch-Ua) & Sec-Fetch-* (NET-13.1.3)
        var clientHintsProfile = clientHintsValidator.Validate(context);

        // 4. Theo dõi tương quan dải mạng Subnet /24 (NET-13.1.3)
        var clientIp = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
                       ?? context.Connection.RemoteIpAddress?.ToString();
        var subnetMetrics = await subnetTracker.TrackSubnetAsync(clientIp, ct);

        // 5. Tổng hợp điểm bất thường toàn mạng (Overall Inconsistency Score)
        float overallScore = 0.0f;
        string? primaryReason = null;

        // Ưu tiên phát hiện bất thường: HTTP/2 > Client Hints > Subnet Cluster > TLS
        if (http2Profile.NetworkInconsistencyScore >= 1.0f)
        {
            overallScore = 1.0f;
            primaryReason = http2Profile.AnomalyReason ?? "HTTP/2 protocol skew detected";
        }
        else if (clientHintsProfile.InconsistencyScore >= 1.0f)
        {
            overallScore = 1.0f;
            primaryReason = clientHintsProfile.AnomalyReason ?? "Client Hints header skew detected";
        }
        else if (subnetMetrics.InconsistencyScore >= 1.0f)
        {
            overallScore = 1.0f;
            primaryReason = subnetMetrics.AnomalyReason ?? "Subnet cluster rate limit exceeded";
        }

        if (overallScore >= 1.0f)
        {
            logger.LogWarning(
                "AntiBot NetworkConsistency: Inconsistency flagged! Score={Score}, Reason={Reason}",
                overallScore, primaryReason);
        }

        return new NetworkConsistencySummary
        {
            Tls = tlsProfile,
            Http2 = http2Profile,
            ClientHints = clientHintsProfile,
            Subnet = subnetMetrics,
            OverallInconsistencyScore = overallScore,
            PrimaryReason = primaryReason
        };
    }
}
