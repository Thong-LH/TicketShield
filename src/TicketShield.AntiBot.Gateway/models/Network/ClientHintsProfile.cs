namespace TicketShield.AntiBot.Gateway.Models.Network;

/// <summary>
/// Kết quả bóc tách và đối soát W3C Client Hints cùng Sec-Fetch-* (NET-13.1.3).
/// </summary>
public record ClientHintsProfile
{
    /// <summary>
    /// Trạng thái phân tích Client Hints.
    /// </summary>
    public ClientHintsStatus Status { get; init; } = ClientHintsStatus.NotApplicable;

    /// <summary>
    /// Chuỗi thô của header Sec-Ch-Ua.
    /// </summary>
    public string? RawSecChUa { get; init; }

    /// <summary>
    /// Danh sách các brand được trích xuất (ví dụ: ["Chromium", "130"], ["Google Chrome", "130"]).
    /// </summary>
    public IReadOnlyList<string> ExtractedBrands { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Giá trị Sec-Ch-Ua-Mobile (?0 hoặc ?1).
    /// </summary>
    public string? SecChUaMobile { get; init; }

    /// <summary>
    /// Giá trị Sec-Ch-Ua-Platform (e.g. "Windows", "macOS", "Android").
    /// </summary>
    public string? SecChUaPlatform { get; init; }

    /// <summary>
    /// Sec-Fetch-Site (e.g. "same-origin", "cross-site", "none").
    /// </summary>
    public string? SecFetchSite { get; init; }

    /// <summary>
    /// Sec-Fetch-Mode (e.g. "cors", "navigate", "no-cors").
    /// </summary>
    public string? SecFetchMode { get; init; }

    /// <summary>
    /// Sec-Fetch-Dest (e.g. "empty", "document").
    /// </summary>
    public string? SecFetchDest { get; init; }

    /// <summary>
    /// Điểm bất thường của Client Hints (0.0 = Sạch, 1.0 = Bất thường/Giả mạo).
    /// </summary>
    public float InconsistencyScore { get; init; } = 0.0f;

    /// <summary>
    /// Lý do bất thường chi tiết nếu có.
    /// </summary>
    public string? AnomalyReason { get; init; }
}
