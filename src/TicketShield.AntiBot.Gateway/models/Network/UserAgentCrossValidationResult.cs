namespace TicketShield.AntiBot.Gateway.Models.Network;

public enum UserAgentMatchLevel
{
    ExactMatch = 0,         // Hai chuỗi trùng khớp 100%
    CanonicalMatch = 1,     // Khớp sau khi chuẩn hóa trailing tokens của WebView (Zalo, FB, TikTok)
    NotApplicable = 2,      // Mobile App hoặc client đặc thù được miễn trừ an toàn
    MinorMismatch = 3,      // Lệch token phụ / phiên bản nhỏ (kích hoạt cờ gian lận & Cầu nối ONNX)
    BlatantMismatch = 4     // Lệch hệ điều hành (Windows vs Linux) hoặc Browser Engine (Chrome vs cURL/Firefox) -> Block 403
}

public record UserAgentCrossValidationResult
{
    public bool IsConsistent { get; init; } = true;
    public bool IsBlatantMismatch { get; init; } = false;
    public UserAgentMatchLevel MatchLevel { get; init; } = UserAgentMatchLevel.ExactMatch;
    public string? Reason { get; init; }
    public string? ClientPlatform { get; init; }
    public string? HeaderPlatform { get; init; }
    public string? ClientBrowser { get; init; }
    public string? HeaderBrowser { get; init; }

    public static UserAgentCrossValidationResult Consistent(UserAgentMatchLevel level = UserAgentMatchLevel.ExactMatch, string? reason = null) =>
        new() { IsConsistent = true, MatchLevel = level, Reason = reason };

    public static UserAgentCrossValidationResult Blatant(string reason, string? clientPlat = null, string? headerPlat = null, string? clientBrowser = null, string? headerBrowser = null) =>
        new()
        {
            IsConsistent = false,
            IsBlatantMismatch = true,
            MatchLevel = UserAgentMatchLevel.BlatantMismatch,
            Reason = reason,
            ClientPlatform = clientPlat,
            HeaderPlatform = headerPlat,
            ClientBrowser = clientBrowser,
            HeaderBrowser = headerBrowser
        };

    public static UserAgentCrossValidationResult Flagged(string reason, string? clientPlat = null, string? headerPlat = null, string? clientBrowser = null, string? headerBrowser = null) =>
        new()
        {
            IsConsistent = false,
            IsBlatantMismatch = false,
            MatchLevel = UserAgentMatchLevel.MinorMismatch,
            Reason = reason,
            ClientPlatform = clientPlat,
            HeaderPlatform = headerPlat,
            ClientBrowser = clientBrowser,
            HeaderBrowser = headerBrowser
        };
}
