using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

public class PayoutTransaction : BaseEntity
{
    public Guid EscrowId { get; set; }
    public Guid? SellerBankAccountId { get; set; }
    public string PayoutCode { get; set; } = string.Empty;
    public string RecipientBankCode { get; set; } = string.Empty;
    public string RecipientAccountNumber { get; set; } = string.Empty;
    public string RecipientAccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;
    public string? BankReferenceCode { get; set; }
    public int RetryCount { get; set; } = 0;
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    // Navigation
    public EscrowTransaction Escrow { get; set; } = null!;
    public UserBankAccount? SellerBankAccount { get; set; }
}
