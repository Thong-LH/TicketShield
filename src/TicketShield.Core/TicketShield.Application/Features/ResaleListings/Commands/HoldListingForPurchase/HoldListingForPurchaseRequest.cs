namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseRequest
{
    public string? PrivateAccessToken { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientIdCard { get; set; }
}
