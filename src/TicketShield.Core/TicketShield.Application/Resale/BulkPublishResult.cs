namespace TicketShield.Application.Resale;

/// <summary>
/// Kết quả đăng bán danh sách vé thành bundle (SCRUM-167)
/// </summary>
public sealed record BulkPublishResult(
    Guid BundleId,
    bool AllOrNothing,
    int TotalListings,
    List<BulkPublishItemResult> Listings);
