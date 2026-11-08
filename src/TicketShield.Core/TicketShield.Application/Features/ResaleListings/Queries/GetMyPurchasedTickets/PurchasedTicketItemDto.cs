namespace TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;

public class PurchasedTicketItemDto
{
    public Guid ListingId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    /// <summary>Live organizer status for this individual pass (for example, USED after gate entry).</summary>
    public string Status { get; set; } = "VALID";
    public string SeatZone { get; set; } = string.Empty;
    public string QrCodeData { get; set; } = string.Empty;
    public string QrCodeImageUrl { get; set; } = string.Empty;
}
