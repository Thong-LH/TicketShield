namespace TicketShield.Application.Features.ResaleListings.Commands.HoldListingForPurchase;

public class HoldListingForPurchaseResponse
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }
    public string ListingStatus { get; set; } = string.Empty;
    public string PaymentReference { get; set; } = string.Empty;
    public string QrImageUrl { get; set; } = string.Empty;
    public string QuickLinkUrl { get; set; } = string.Empty;
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal ResalePrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal SellerFee { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public decimal NetSellerPayout { get; set; }
    public DateTimeOffset UnlockAt { get; set; }
    public int HoldDurationSeconds { get; set; }
}
