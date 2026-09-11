namespace TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;

public class CancelResaleListingResponse
{
    public Guid ListingId { get; set; }
    public string OriginalTicketCode { get; set; } = string.Empty;
    public string ListingStatus { get; set; } = string.Empty;
    public DateTimeOffset CancelledAt { get; set; }
}
