namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

/// <summary>
/// Per-listing detail within a bundle hold response (BE-CORE-5.2.3).
/// FE-5.2.6 uses this to display individual ticket info in the bundle checkout.
/// </summary>
public class BundleHeldItemDto
{
    public Guid ListingId { get; set; }
    public decimal ResalePrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal SellerFee { get; set; }
}
