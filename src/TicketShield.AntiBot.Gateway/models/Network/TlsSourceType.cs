namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Nguồn trích xuất thông tin TLS Profile trong Gateway.
/// </summary>
public enum TlsSourceType
{
    /// <summary>
    /// Không có thông tin TLS (kết nối HTTP thuần hoặc không trích xuất được).
    /// </summary>
    None = 0,

    /// <summary>
    /// Trích xuất trực tiếp từ ASP.NET Core Kestrel (ITlsHandshakeFeature / ITlsApplicationProtocolFeature).
    /// Dùng khi Gateway trực tiếp chấm dứt kết nối TLS (Direct Termination).
    /// </summary>
    KestrelDirect = 1,

    /// <summary>
    /// Trích xuất từ Request Header (X-JA4, CF-JA4) do Reverse Proxy ngoài (Cloudflare, Nginx, Envoy) chuyển tiếp.
    /// </summary>
    ProxyHeaderJa4 = 2
}
