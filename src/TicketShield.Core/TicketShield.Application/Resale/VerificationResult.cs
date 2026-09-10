namespace TicketShield.Application.Resale;

/// <summary>
/// Kết quả phản hồi trạng thái của phiên xác thực vé
/// </summary>
public sealed record VerificationResult(
    string VerificationId,
    string Status,
    string? OperationId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ResendAfter,
    string? DeliveryState,
    long? OriginalPrice,
    Guid? ListingId,
    string? PrivateAccessToken = null);
