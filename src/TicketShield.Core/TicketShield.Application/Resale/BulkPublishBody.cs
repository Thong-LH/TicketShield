namespace TicketShield.Application.Resale;

/// <summary>
/// Yêu cầu đăng bán danh sách vé cùng lúc thành một bundle (SCRUM-167)
/// </summary>
public sealed record BulkPublishBody(
    List<BulkPublishItem> Items,
    bool AllOrNothing = false);
