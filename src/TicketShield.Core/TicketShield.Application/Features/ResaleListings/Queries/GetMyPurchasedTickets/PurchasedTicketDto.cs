namespace TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;

public class PurchasedTicketDto
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventVenue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }
    public string TierName { get; set; } = string.Empty;
    public string SeatZone { get; set; } = string.Empty;
    public string TicketPassCode { get; set; } = string.Empty;
    public decimal TotalAmountPaid { get; set; }
    public string Status { get; set; } = "VALID";
    public string? PaymentReference { get; set; }
    public DateTimeOffset? HoldExpiresAt { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string RecipientEmail { get; set; } = string.Empty;
    public string QrCodeData { get; set; } = string.Empty;
    public string QrCodeImageUrl { get; set; } = string.Empty;
    public DateTimeOffset PurchasedAt { get; set; }
}
