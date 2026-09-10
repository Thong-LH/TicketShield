namespace TicketShield.Application.Resale;

/// <summary>
/// Kết quả phản hồi danh sách vé niêm yết công khai trên chợ vé
/// </summary>
public sealed record ListingResult(
    Guid Id,
    Guid EventId,
    Guid TierId,
    long ResalePrice,
    string Currency = "VND");
