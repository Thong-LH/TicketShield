using Microsoft.AspNetCore.Http;
using TicketShield.AntiBot.Gateway.Models.Network;

namespace TicketShield.AntiBot.Gateway.Services.Network;

/// <summary>
/// Giao diện trích xuất hồ sơ TLS (TLS Profile) từ HTTP Context.
/// Hỗ trợ cả 2 kịch bản: Direct TLS termination tại Kestrel và External Reverse Proxy qua Header X-JA4.
/// </summary>
public interface ITlsProfileExtractor
{
    /// <summary>
    /// Trích xuất hồ sơ TLS chuẩn hóa của request hiện tại.
    /// Đảm bảo không ném ngoại lệ (Exception-safe) và tuân thủ SLA < 0.1ms.
    /// </summary>
    /// <param name="context">HttpContext của request đang xử lý.</param>
    /// <returns>Đối tượng <see cref="TlsProfile"/> chứa đầy đủ thông tin giao thức và mã hóa.</returns>
    TlsProfile Extract(HttpContext context);
}
