namespace TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;

public class PurchasedTicketItemDto
{
    public Guid ListingId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public string SeatZone { get; set; } = string.Empty;
    public string QrCodeData { get; set; } = string.Empty;
    public string QrCodeImageUrl { get; set; } = string.Empty;
}
