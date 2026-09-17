namespace TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;

public class ReleaseListingHoldResponse
{
    public Guid ListingId { get; set; }
    public string ListingStatus { get; set; } = string.Empty;
    public DateTimeOffset ReleasedAt { get; set; }
}
