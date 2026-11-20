namespace TicketShield.AntiBot.Gateway.Models;

/// <summary>
/// Chế độ thực thi chấm điểm chống bot tại Gateway (Epic US-13.3, Jira SCRUM-382).
/// </summary>
public enum AntiBotExecutionMode
{
    /// <summary>
    /// Chế độ Shadow Mode (Chạy song song v1 và v2, v1 ra quyết định thực tế bảo toàn thanh toán, v2 chạy ngầm đối chứng).
    /// </summary>
    Shadow,

    /// <summary>
    /// Chế độ Enforce (Mô hình chỉ định ra quyết định trực tiếp áp dụng hình phạt chặn hoặc thử thách lên traffic).
    /// </summary>
    Enforce,

    /// <summary>
    /// Chế độ Audit / Log-Only (Chỉ ghi log quan sát, không bao giờ chặn lưu lượng khách hàng).
    /// </summary>
    Audit
}
