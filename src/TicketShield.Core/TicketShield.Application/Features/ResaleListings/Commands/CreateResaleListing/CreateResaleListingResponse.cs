namespace TicketShield.Application.Features.ResaleListings.Commands.CreateResaleListing;

public class CreateResaleListingResponse
{
    public Guid ListingId { get; set; }
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
    public string OriginalTicketCode { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public decimal ResalePrice { get; set; }
    public bool IsPrivate { get; set; }
    public string? PrivateAccessToken { get; set; }
    public string? ShareUrl { get; set; }
    public string VerificationStatus { get; set; } = string.Empty;
    public string ListingStatus { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
