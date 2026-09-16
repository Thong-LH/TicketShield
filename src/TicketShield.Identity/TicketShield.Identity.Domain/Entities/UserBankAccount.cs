using TicketShield.Identity.Domain.Common;

namespace TicketShield.Identity.Domain.Entities;

public class UserBankAccount : BaseEntity
{
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsDefault { get; set; } = false;

    // Navigation properties
    public User User { get; set; } = null!;
}
