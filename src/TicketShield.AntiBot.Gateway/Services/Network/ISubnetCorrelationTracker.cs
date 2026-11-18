using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện dịch vụ theo dõi tương quan dải mạng IPv4 /24 và IPv6 /48 (NET-13.1.3).
/// Phát hiện cụm residential proxy botnet tấn công checkout đồng thời.
/// </summary>
public interface ISubnetCorrelationTracker
{
    /// <summary>
    /// Ghi nhận và phân tích tần suất cũng như độ phân tán IP trong dải subnet của client.
    /// </summary>
    Task<SubnetRateMetrics> TrackSubnetAsync(
        string? clientIp,
        CancellationToken ct = default);

    /// <summary>
    /// Trích xuất CIDR đại diện cho dải mạng (/24 cho IPv4, /48 cho IPv6).
    /// </summary>
    string ExtractSubnetCidr(string? clientIp);

    /// <summary>
    /// Kiểm tra IP có thuộc dải mạng nội bộ hoặc Loopback (miễn trừ kiểm tra để phục vụ dev/test) hay không.
    /// </summary>
    bool IsPrivateOrLoopback(string? clientIp);
}
