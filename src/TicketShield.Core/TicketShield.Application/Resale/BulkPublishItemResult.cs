namespace TicketShield.Application.Resale;

/// <summary>
/// Kết quả đăng bán từng vé trong bundle (SCRUM-167)
/// </summary>
public sealed record BulkPublishItemResult(
    Guid ListingId,
    string VerificationId,
    string Status,
    string? PrivateAccessToken = null);
