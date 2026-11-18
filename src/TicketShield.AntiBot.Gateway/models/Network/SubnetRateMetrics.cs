namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Thống kê tần suất và cụm IP theo dải mạng IPv4 /24 hoặc IPv6 /48 (NET-13.1.3).
/// </summary>
public record SubnetRateMetrics
{
    /// <summary>
    /// Địa chỉ IP gốc của client.
    /// </summary>
    public string ClientIp { get; init; } = string.Empty;

    /// <summary>
    /// Dải mạng CIDR (e.g. "198.51.100.0/24" hoặc "2001:db8::/48").
    /// </summary>
    public string SubnetCidr { get; init; } = string.Empty;

    /// <summary>
    /// Tổng số request trong cửa sổ trượt (10 giây) thuộc dải subnet này.
    /// </summary>
    public int SubnetRequestCount { get; init; } = 0;

    /// <summary>
    /// Số lượng địa chỉ IP duy nhất trong dải subnet đã gửi request trong cửa sổ trượt.
    /// </summary>
    public int DistinctIpCount { get; init; } = 0;

    /// <summary>
    /// Đánh dấu IP được miễn trừ kiểm tra (Localhost / Private RFC 1918) để bảo đảm dev/test không bị chặn.
    /// </summary>
    public bool IsExempt { get; init; } = false;

    /// <summary>
    /// Phát hiện bất thường dạng cụm botnet hoặc dồn dập request trên dải subnet.
    /// </summary>
    public bool IsAnomaly { get; init; } = false;

    /// <summary>
    /// Điểm rủi ro bất thường subnet (0.0 = Bình thường, 1.0 = Cụm botnet tấn công).
    /// </summary>
    public float InconsistencyScore { get; init; } = 0.0f;

    /// <summary>
    /// Lý do bất thường chi tiết nếu có.
    /// </summary>
    public string? AnomalyReason { get; init; }
}
