namespace MockOrganizer.API.Services;

public interface ISessionTokenService
{
    /// <summary>
    /// Phát hành session token có chữ ký HMAC-SHA256 chứa timestamp cấp phát.
    /// </summary>
    string GenerateSessionToken(Guid tierId, DateTimeOffset? issuedAt = null);

    /// <summary>
    /// Xác thực session token và trích xuất thời điểm cấp phát (issued_at) cùng tier_id.
    /// </summary>
    (bool IsValid, DateTimeOffset IssuedAt, Guid TierId) ValidateSessionToken(string? token);
}
