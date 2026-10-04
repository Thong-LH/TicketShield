namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

/// <summary>
/// Vé đã cấp thành công cho Buyer sau khi BTC Organizer chuyển tên.
/// Mỗi vé giữ mã và QR riêng để không bao giờ phát sinh mã vé giả hay chuỗi QR gộp không quét được.
/// </summary>
public sealed record IssuedTicket(
    Guid ListingId,
    string NewCode,
    string QrCodeData,
    string? SeatZone);
