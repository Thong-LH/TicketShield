using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Hồ sơ TLS Profile chuẩn hóa của request tới Gateway, tổng hợp từ kết nối trực tiếp Kestrel hoặc Proxy Header.
/// Làm dữ liệu đầu vào cho quy tắc đối chiếu tính nhất quán giao thức (NET-13.1.2).
/// </summary>
public record TlsProfile
{
    /// <summary>
    /// Phiên bản giao thức TLS đã đàm phán (ví dụ: "Tls13", "Tls12", "None").
    /// </summary>
    [JsonPropertyName("tlsVersion")]
    public string? TlsVersion { get; init; }

    /// <summary>
    /// Tên bộ mã hóa (Cipher Suite) đã đàm phán (ví dụ: "TLS_AES_128_GCM_SHA256", "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256").
    /// </summary>
    [JsonPropertyName("negotiatedCipherSuite")]
    public string? NegotiatedCipherSuite { get; init; }

    /// <summary>
    /// Giao thức tầng ứng dụng ALPN (ví dụ: "h2", "http/1.1").
    /// </summary>
    [JsonPropertyName("alpn")]
    public string? Alpn { get; init; }

    /// <summary>
    /// Thông tin phân giải JA4 Fingerprint (nếu nhận được qua reverse proxy ngoài).
    /// </summary>
    [JsonPropertyName("ja4")]
    public ParsedJa4? Ja4 { get; init; }

    /// <summary>
    /// Nguồn trích xuất thông tin TLS (KestrelDirect, ProxyHeaderJa4 hoặc None).
    /// </summary>
    [JsonPropertyName("source")]
    public TlsSourceType Source { get; init; } = TlsSourceType.None;

    /// <summary>
    /// Cho biết request có đến từ kết nối bảo mật (HTTPS / TLS) hợp lệ hay không.
    /// </summary>
    [JsonPropertyName("isSecureConnection")]
    public bool IsSecureConnection { get; init; }

    /// <summary>
    /// Thời điểm trích xuất hồ sơ TLS (UTC).
    /// </summary>
    [JsonPropertyName("extractedAtUtc")]
    public DateTime ExtractedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Giá trị mặc định khi request đến qua HTTP thuần không mã hóa hoặc không có thông tin TLS.
    /// </summary>
    public static readonly TlsProfile NonTls = new()
    {
        TlsVersion = null,
        NegotiatedCipherSuite = null,
        Alpn = null,
        Ja4 = null,
        Source = TlsSourceType.None,
        IsSecureConnection = false
    };
}
