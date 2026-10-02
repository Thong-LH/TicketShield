namespace TicketShield.Application.Resale;

/// <summary>
/// Thông tin từng vé trong batch đăng bán (SCRUM-167 / BE-CORE-5.2.2)
/// </summary>
public sealed record BulkPublishItem(
    string VerificationId,
    long ResalePrice,
    bool IsPrivate = false,
    Guid? IdempotencyKey = null);
