namespace MockOrganizer.API.Models;

/// <summary>
/// Kết quả xác thực chi tiết của SessionToken (US-13.2.3, SCRUM-376).
/// Hỗ trợ cả Token v1 (3 segments) và Token v2 (4 segments nhúng One-Time Nonce).
/// </summary>
public record SessionTokenValidationResult
{
    public bool IsValid { get; init; } = false;
    public DateTimeOffset IssuedAt { get; init; } = DateTimeOffset.MinValue;
    public Guid TierId { get; init; } = Guid.Empty;
    public string? Nonce { get; init; }
    public DateTimeOffset SessionStart { get; init; } = DateTimeOffset.MinValue;
    public int RefreshCount { get; init; } = 0;
    public int TokenVersion { get; init; } = 1;
    public string? FailureReason { get; init; }

    public static SessionTokenValidationResult Success(
        DateTimeOffset issuedAt,
        Guid tierId,
        string? nonce,
        int version,
        DateTimeOffset? sessionStart = null,
        int refreshCount = 0) =>
        new()
        {
            IsValid = true,
            IssuedAt = issuedAt,
            TierId = tierId,
            Nonce = nonce,
            TokenVersion = version,
            SessionStart = sessionStart ?? issuedAt,
            RefreshCount = refreshCount
        };

    public static SessionTokenValidationResult Failed(string reason) =>
        new()
        {
            IsValid = false,
            FailureReason = reason
        };
}
