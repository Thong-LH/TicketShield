using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class UserBankAccount : BaseEntity
{
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = false;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<PayoutTransaction> Payouts { get; set; } = new List<PayoutTransaction>();
}
