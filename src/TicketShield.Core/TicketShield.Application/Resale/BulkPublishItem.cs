namespace TicketShield.Application.Resale;

/// <summary>
/// Thông tin từng vé trong batch đăng bán (SCRUM-167)
/// </summary>
public sealed record BulkPublishItem(
    string VerificationId,
    Guid IdempotencyKey,
    long ResalePrice,
    bool IsPrivate = false);
