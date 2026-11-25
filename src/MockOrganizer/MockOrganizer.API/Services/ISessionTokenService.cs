using MockOrganizer.API.Models;

namespace MockOrganizer.API.Services;

public interface ISessionTokenService
{
    /// <summary>
    /// Phát hành session token v1 có chữ ký HMAC-SHA256 chứa timestamp cấp phát.
    /// </summary>
    string GenerateSessionToken(Guid tierId, DateTimeOffset? issuedAt = null);

    /// <summary>
    /// Phát hành SessionToken v2 có nhúng One-Time Cryptographic Nonce và TTL 300s (US-13.2.3, SCRUM-376).
    /// </summary>
    (string Token, string Nonce, DateTimeOffset IssuedAt, int ExpiresInSeconds) GenerateSessionTokenWithNonce(
        Guid tierId,
        DateTimeOffset? issuedAt = null,
        int expiresInSeconds = 300);

    /// <summary>
    /// Phát hành SessionToken v3 bảo toàn session_start (Session Continuity) và giới hạn refresh (US-14.1).
    /// </summary>
    (string Token, string Nonce, DateTimeOffset IssuedAt, DateTimeOffset SessionStart, int ExpiresInSeconds) GenerateSessionTokenV3(
        Guid tierId,
        DateTimeOffset? sessionStart = null,
        DateTimeOffset? issuedAt = null,
        int refreshCount = 0,
        int expiresInSeconds = 300);

    /// <summary>
    /// Xác thực session token và trích xuất thời điểm cấp phát (issued_at) cùng tier_id (tương thích ngược).
    /// </summary>
    (bool IsValid, DateTimeOffset IssuedAt, Guid TierId) ValidateSessionToken(string? token);

    /// <summary>
    /// Xác thực session token v2 chi tiết có trích xuất Nonce (US-13.2.3).
    /// </summary>
    SessionTokenValidationResult ValidateSessionTokenV2(string? token);
}
