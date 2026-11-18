using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện phân tích và kiểm tra các tham số trong HTTP/2 SETTINGS frame (NET-13.1.2).
/// Chuẩn đặc tả: RFC 7540 / Akamai HTTP/2 Fingerprint.
/// </summary>
public interface IHttp2SettingsInspector
{
    /// <summary>
    /// Bóc tách các tham số SETTINGS từ HttpContext (qua Proxy Header hoặc cấu hình Kestrel).
    /// </summary>
    /// <param name="context">HttpContext của request.</param>
    /// <returns>Đối tượng <see cref="Http2SettingsFrame"/>.</returns>
    Http2SettingsFrame ExtractSettings(HttpContext context);

    /// <summary>
    /// Thử phân tích chuỗi SETTINGS thô dạng "1:65536;2:0;4:6291456;6:262144".
    /// </summary>
    /// <param name="rawSettings">Chuỗi SETTINGS thô.</param>
    /// <param name="settings">Kết quả phân tích nếu thành công.</param>
    /// <returns>True nếu phân tích thành công; False nếu sai cú pháp.</returns>
    bool TryParseSettingsString(string? rawSettings, out Http2SettingsFrame settings);

    /// <summary>
    /// Đối chiếu các tham số SETTINGS frame với chuỗi User-Agent khai báo.
    /// </summary>
    /// <param name="settings">Đối tượng SETTINGS đã phân tích.</param>
    /// <param name="userAgent">Chuỗi User-Agent của client.</param>
    /// <param name="anomalyReason">Lý do bất thường nếu phát hiện bot giả lập.</param>
    /// <returns>True nếu nhất quán; False nếu có dấu hiệu bot giả mạo.</returns>
    bool IsSettingsConsistentWithBrowser(Http2SettingsFrame settings, string? userAgent, out string? anomalyReason);
}
