using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Dịch vụ đối chiếu tính nhất quán giao thức HTTP/2 (NET-13.1.2).
/// Tổng hợp kết quả từ PseudoHeaderInspector, Http2SettingsInspector và TLS Profile (NET-13.1.1).
/// </summary>
public interface IHttp2ConsistencyService
{
    /// <summary>
    /// Thực hiện thanh tra và tính điểm bất thường mạng (network_inconsistency_score).
    /// </summary>
    /// <param name="context">HttpContext của request.</param>
    /// <param name="userAgent">Chuỗi User-Agent khai báo của client.</param>
    /// <returns>Đối tượng <see cref="Http2Profile"/> hoàn chỉnh.</returns>
    Http2Profile Inspect(HttpContext context, string? userAgent);
}
