using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Transactions.Models;

/// <summary>
/// DTO chi tiết giao dịch Ký quỹ (Escrow) kết hợp thông tin Sổ cái giải ngân (Payout Ledger)
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-334 (BE-11.1.1)
/// </summary>
public class AdminTransactionDto
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }
    public Guid? BundleId { get; set; }

    // Event & Ticket info
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string OriginalTicketCode { get; set; } = string.Empty;
    public string? NewTicketCode { get; set; }

    // Parties
    public Guid BuyerId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public string? BuyerPhone { get; set; }

    public Guid SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string SellerEmail { get; set; } = string.Empty;
    public string? SellerPhone { get; set; }

    // Financial breakdown
    public decimal OriginalTicketPrice { get; set; }
    public decimal BuyerFee { get; set; }
    public decimal SellerFee { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public decimal NetSellerPayout { get; set; }

    // References
    public string? PaymentReference { get; set; }
    public string? BankTransactionReference { get; set; }

    // Status & Milestones
    public EscrowStatus Status { get; set; }
    public bool InSettlementBuffer { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }
    public DateTimeOffset? UnlockAt { get; set; }
    public DateTimeOffset? DisputeDeadline { get; set; }
    public bool DisputeWaived { get; set; }
    public DateTimeOffset? DisputeWaivedAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Payout Ledger info
    public Guid? PayoutId { get; set; }
    public string? PayoutCode { get; set; }
    public PayoutStatus? PayoutStatus { get; set; }
    public decimal? PayoutAmount { get; set; }
    public string? RecipientBankCode { get; set; }
    public string? RecipientAccountNumber { get; set; }
    public string? RecipientAccountName { get; set; }
    public string? BankReferenceCode { get; set; }
    public DateTimeOffset? PayoutProcessedAt { get; set; }
    public string? PayoutErrorMessage { get; set; }
    public int PayoutRetryCount { get; set; }

    // Dispute reference
    public Guid? DisputeId { get; set; }
    public DisputeStatus? DisputeStatus { get; set; }
}

