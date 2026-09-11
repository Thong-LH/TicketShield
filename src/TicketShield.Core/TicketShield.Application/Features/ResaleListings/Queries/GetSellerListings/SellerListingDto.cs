using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;

public class SellerListingDto
{
    public Guid ListingId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string EventVenue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }

    public Guid TierId { get; set; }
    public string TierName { get; set; } = string.Empty;

    public string OriginalTicketCode { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }

    public bool IsPrivate { get; set; }
    public string? PrivateAccessToken { get; set; }
    public string? ShareUrl { get; set; }

    public string VerificationStatus { get; set; } = string.Empty;
    public string ListingStatus { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
