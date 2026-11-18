using System.Text.Json.Serialization;

namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Đại diện cho cấu trúc JA4 Fingerprint đã phân rã theo chuẩn Fox-IT / John Althouse:
/// Định dạng: {JA4_a}_{JA4_b}_{JA4_c}
/// Ví dụ: t13d1516h2_8daaf6152771_02711d0e519e
/// </summary>
public record ParsedJa4
{
    /// <summary>
    /// Chuỗi JA4 nguyên bản nhận được.
    /// </summary>
    [JsonPropertyName("rawJa4")]
    public string RawJa4 { get; init; } = string.Empty;

    /// <summary>
    /// Giao thức mạng tầng transport: 't' (TCP/TLS), 'q' (QUIC/HTTP3), 'd' (DTLS).
    /// </summary>
    [JsonPropertyName("protocol")]
    public char Protocol { get; init; } = 't';

    /// <summary>
    /// Phiên bản TLS: "13" (TLS 1.3), "12" (TLS 1.2), "11", "10", "s3" (SSLv3).
    /// </summary>
    [JsonPropertyName("tlsVersion")]
    public string TlsVersion { get; init; } = "13";

    /// <summary>
    /// Dạng SNI: 'd' (Domain Name), 'i' (IP Address).
    /// </summary>
    [JsonPropertyName("sniType")]
    public char SniType { get; init; } = 'd';

    /// <summary>
    /// Số lượng Cipher Suites client gửi trong TLS Client Hello (tối đa 99).
    /// </summary>
    [JsonPropertyName("cipherCount")]
    public int CipherCount { get; init; }

    /// <summary>
    /// Số lượng TLS Extensions client gửi (tối đa 99).
    /// </summary>
    [JsonPropertyName("extensionCount")]
    public int ExtensionCount { get; init; }

    /// <summary>
    /// Giao thức ALPN đàm phán đầu tiên: "h2", "h1", "00" (nếu không có ALPN).
    /// </summary>
    [JsonPropertyName("alpn")]
    public string Alpn { get; init; } = string.Empty;

    /// <summary>
    /// 12 ký tự hex SHA256 đại diện danh sách Cipher Suites đã sắp xếp theo thứ tự số học.
    /// </summary>
    [JsonPropertyName("cipherHash")]
    public string CipherHash { get; init; } = string.Empty;

    /// <summary>
    /// 12 ký tự hex SHA256 đại diện danh sách Extensions + Signature Algorithms.
    /// </summary>
    [JsonPropertyName("extensionHash")]
    public string ExtensionHash { get; init; } = string.Empty;

    /// <summary>
    /// Trạng thái chuỗi JA4 có hợp lệ về mặt cú pháp và độ dài hay không.
    /// </summary>
    [JsonPropertyName("isValid")]
    public bool IsValid { get; init; }

    /// <summary>
    /// Đối tượng rỗng đại diện cho trường hợp không có hoặc phân tích thất bại.
    /// </summary>
    public static readonly ParsedJa4 Empty = new()
    {
        RawJa4 = string.Empty,
        IsValid = false
    };
}
