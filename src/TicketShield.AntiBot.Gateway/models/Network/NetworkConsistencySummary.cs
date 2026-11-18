namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Tổng hợp kết quả đánh giá tính nhất quán mạng toàn diện (NET-13.1.1, NET-13.1.2, NET-13.1.3).
/// </summary>
public record NetworkConsistencySummary
{
    /// <summary>
    /// Hồ sơ TLS & JA4 (NET-13.1.1).
    /// </summary>
    public TlsProfile Tls { get; init; } = null!;

    /// <summary>
    /// Hồ sơ HTTP/2 Settings & Pseudo-Headers (NET-13.1.2).
    /// </summary>
    public Http2Profile Http2 { get; init; } = null!;

    /// <summary>
    /// Hồ sơ đối soát Client Hints & Sec-Fetch headers (NET-13.1.3).
    /// </summary>
    public ClientHintsProfile ClientHints { get; init; } = null!;

    /// <summary>
    /// Chỉ số tương quan dải mạng Subnet /24 (NET-13.1.3).
    /// </summary>
    public SubnetRateMetrics Subnet { get; init; } = null!;

    /// <summary>
    /// Điểm bất thường mạng tổng hợp (0.0 = Nhất quán hoàn toàn, 1.0 = Bị phát hiện giả mạo/bot).
    /// </summary>
    public float OverallInconsistencyScore { get; init; } = 0.0f;

    /// <summary>
    /// Lý do chính gây ra bất thường (nếu có).
    /// </summary>
    public string? PrimaryReason { get; init; }

    /// <summary>
    /// Cờ xác định có phát hiện bất thường giao thức hoặc mạng không.
    /// </summary>
    public bool HasInconsistency => OverallInconsistencyScore >= 1.0f;
}
