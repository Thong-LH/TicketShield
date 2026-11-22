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
    string? PrivateAccessToken = null,
    decimal? MarkupPercent = null,
    long? PriceCeiling = null,
    string? EventName = null,
    string? EventVenue = null,
    DateTimeOffset? EventStartAt = null,
    string? TierName = null,
    string? SeatZone = null,
    string? OrganizerName = null);
