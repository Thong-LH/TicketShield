namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Trạng thái đối soát W3C Client Hints (Sec-Ch-Ua, Sec-Fetch-*) với User-Agent (NET-13.1.3).
/// </summary>
public enum ClientHintsStatus
{
    /// <summary>
    /// Safari, Firefox hoặc Mobile App hợp lệ (không bắt buộc Client Hints).
    /// </summary>
    NotApplicable = 0,

    /// <summary>
    /// Khớp 100% giữa User-Agent và Client Hints / Sec-Fetch headers.
    /// </summary>
    Consistent = 1,

    /// <summary>
    /// User-Agent khai báo là Chromium (Chrome/Edge) nhưng thiếu hoàn toàn Sec-Ch-Ua (Dấu hiệu curl_cffi/requests).
    /// </summary>
    MissingHints = 2,

    /// <summary>
    /// Thương hiệu (Brand) trong Sec-Ch-Ua không khớp với User-Agent (ví dụ: UA Chrome nhưng hints thiếu Google Chrome/Chromium).
    /// </summary>
    BrandMismatch = 3,

    /// <summary>
    /// Phiên bản lớn (Major version) trong Sec-Ch-Ua lệch với User-Agent.
    /// </summary>
    VersionMismatch = 4,

    /// <summary>
    /// Nền tảng hệ điều hành (Platform) trong Sec-Ch-Ua-Platform lệch với User-Agent (ví dụ: UA Windows nhưng platform Linux).
    /// </summary>
    PlatformMismatch = 5,

    /// <summary>
    /// Cờ di động (Sec-Ch-Ua-Mobile) lệch với User-Agent (?1 vs ?0).
    /// </summary>
    MobileFlagMismatch = 6,

    /// <summary>
    /// Bất thường hoặc thiếu các header điều hướng bảo mật Sec-Fetch-*.
    /// </summary>
    SecFetchMismatch = 7
}
