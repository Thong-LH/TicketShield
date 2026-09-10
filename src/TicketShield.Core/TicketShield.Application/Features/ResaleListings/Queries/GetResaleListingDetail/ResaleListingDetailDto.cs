namespace TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

public class ResaleListingDetailDto
{
    public Guid ListingId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventVenue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }
    public Guid TierId { get; set; }
    public string TierName { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public bool IsPrivate { get; set; }
    public string MaskedTicketCode { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string ListingStatus { get; set; } = string.Empty;
    public Guid SellerId { get; set; }
    public string SellerFullName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
