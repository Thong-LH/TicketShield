namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Trạng thái kết quả kiểm tra tính nhất quán giao thức HTTP/2 (NET-13.1.2).
/// </summary>
public enum Http2InspectionStatus
{
    /// <summary>
    /// Không áp dụng kiểm tra (Request đến qua HTTP/1.1 hoặc môi trường local development không có HTTPS/H2).
    /// Grace period: Điểm phạt rủi ro = 0.0f để đảm bảo không chặn nhầm người dùng dev.
    /// </summary>
    NotApplicable = 0,

    /// <summary>
    /// Nhất quán hoàn toàn: Thứ tự Pseudo-headers và tham số SETTINGS frame khớp 100% với User-Agent khai báo.
    /// Điểm phạt rủi ro = 0.0f (Human Browser).
    /// </summary>
    Consistent = 1,

    /// <summary>
    /// Bất thường / Không nhất quán: Khai báo User-Agent trình duyệt (ví dụ Chrome 130) nhưng gửi sai
    /// cấu hình SETTINGS hoặc thứ tự pseudo-headers của engine khác hoặc bot HTTP client.
    /// Điểm phạt rủi ro = 1.0f (Bot Detected).
    /// </summary>
    Inconsistent = 2,

    /// <summary>
    /// Dữ liệu frame SETTINGS hoặc pseudo-header bị dị dạng, sai định dạng hoặc cố tình làm nhiễu.
    /// </summary>
    Malformed = 3
}
