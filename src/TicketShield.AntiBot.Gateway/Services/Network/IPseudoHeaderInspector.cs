using Microsoft.AspNetCore.Http;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện bóc tách và kiểm tra thứ tự Pseudo-Headers trong HTTP/2 (NET-13.1.2).
/// Các pseudo-header gồm: :method (m), :authority (a), :scheme (s), :path (p).
/// </summary>
public interface IPseudoHeaderInspector
{
    /// <summary>
    /// Bóc tách chuỗi thứ tự pseudo-headers từ request (qua Proxy header hoặc Kestrel feature).
    /// Chuỗi trả về ở định dạng chuẩn viết tắt, ví dụ: "m,a,s,p".
    /// </summary>
    /// <param name="context">HttpContext của request.</param>
    /// <returns>Chuỗi thứ tự pseudo-header (hoặc string.Empty nếu không phải HTTP/2).</returns>
    string ExtractPseudoHeaderOrder(HttpContext context);

    /// <summary>
    /// Đối chiếu thứ tự pseudo-headers với chuỗi User-Agent khai báo để phát hiện bot giả lập.
    /// </summary>
    /// <param name="order">Thứ tự pseudo-headers trích xuất được (e.g. "m,a,s,p").</param>
    /// <param name="userAgent">Chuỗi User-Agent của client.</param>
    /// <param name="anomalyReason">Lý do bất thường nếu phát hiện giả mạo.</param>
    /// <returns>True nếu nhất quán với trình duyệt khai báo; False nếu có dấu hiệu bot giả mạo.</returns>
    bool IsOrderConsistentWithBrowser(string? order, string? userAgent, out string? anomalyReason);
}
