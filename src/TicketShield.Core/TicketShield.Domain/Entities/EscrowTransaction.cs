using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

public class EscrowTransaction : BaseEntity
{
    public Guid ListingId { get; set; }
    public Guid BuyerId { get; set; }
    public Guid SellerId { get; set; }
    public decimal OriginalTicketPrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal SellerFee { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public decimal NetSellerPayout { get; set; }
    public string? PaymentReference { get; set; }
    public string? BankTransactionReference { get; set; }
    public EscrowStatus Status { get; set; } = EscrowStatus.Pending;
    public DateTimeOffset? UnlockAt { get; set; }
    public DateTimeOffset? DisputeDeadline { get; set; }
    public string? RecipientName { get; set; }
    public string? RecipientEmail { get; set; }
    public string? RecipientIdCard { get; set; }
    public bool InSettlementBuffer { get; set; }
    public string? NewTicketCode { get; set; }
    public string? QrCodeData { get; set; }

    // Navigation
    public ResaleListing Listing { get; set; } = null!;
    public ShadowUser Buyer { get; set; } = null!;
    public ShadowUser Seller { get; set; } = null!;
    public PayoutTransaction? PayoutTransaction { get; set; }
    public Dispute? Dispute { get; set; }

    public static DateTimeOffset ComputeSettlementUnlockAt(DateTimeOffset utcNow, DateTimeOffset eventStartAt)
    {
        var plus24h = utcNow.AddHours(24);
        var cutoff = eventStartAt.AddHours(-2);
        return plus24h <= cutoff ? plus24h : cutoff;
    }
}
