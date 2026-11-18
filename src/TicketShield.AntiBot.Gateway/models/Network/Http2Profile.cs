using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Hồ sơ kiểm tra tính nhất quán giao thức HTTP/2 của request (NET-13.1.2).
/// Tổng hợp thứ tự Pseudo-Headers và các thông số trong SETTINGS frame.
/// </summary>
public record Http2Profile
{
    /// <summary>
    /// Cho biết request có thực sự được vận chuyển qua giao thức HTTP/2 hay không.
    /// </summary>
    [JsonPropertyName("isHttp2Connection")]
    public bool IsHttp2Connection { get; init; }

    /// <summary>
    /// Chuỗi thứ tự các Pseudo-headers (ví dụ: "m,a,s,p" cho Chrome, "m,p,a,s" cho Firefox).
    /// m = :method, a = :authority, s = :scheme, p = :path.
    /// </summary>
    [JsonPropertyName("pseudoHeaderOrder")]
    public string PseudoHeaderOrder { get; init; } = string.Empty;

    /// <summary>
    /// Các tham số trích xuất từ HTTP/2 SETTINGS frame.
    /// </summary>
    [JsonPropertyName("settings")]
    public Http2SettingsFrame? Settings { get; init; }

    /// <summary>
    /// Trạng thái kết quả đối chiếu tính nhất quán.
    /// </summary>
    [JsonPropertyName("status")]
    public Http2InspectionStatus Status { get; init; } = Http2InspectionStatus.NotApplicable;

    /// <summary>
    /// Điểm bất thường mạng (network_inconsistency_score theo Hợp đồng US-13.1).
    /// Giá trị 0.0f nếu khớp hoặc không áp dụng; 1.0f nếu phát hiện bot giả lập.
    /// </summary>
    [JsonPropertyName("networkInconsistencyScore")]
    public float NetworkInconsistencyScore { get; init; } = 0.0f;

    /// <summary>
    /// Lý do phát hiện bất thường nếu có (e.g. "User-Agent Chrome 130 but Pseudo-Header order is 'm,p,a,s' (Firefox)").
    /// </summary>
    [JsonPropertyName("anomalyReason")]
    public string? AnomalyReason { get; init; }

    /// <summary>
    /// Thời điểm thực hiện kiểm tra (UTC).
    /// </summary>
    [JsonPropertyName("inspectedAtUtc")]
    public DateTime InspectedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Giá trị mặc định an toàn cho môi trường Localhost HTTP/1.1 (Grace Period, không phạt điểm).
    /// </summary>
    public static readonly Http2Profile DefaultNotApplicable = new()
    {
        IsHttp2Connection = false,
        PseudoHeaderOrder = string.Empty,
        Settings = null,
        Status = Http2InspectionStatus.NotApplicable,
        NetworkInconsistencyScore = 0.0f,
        AnomalyReason = "Connection is HTTP/1.1 or Local Dev (Grace Period applied)."
    };
}
