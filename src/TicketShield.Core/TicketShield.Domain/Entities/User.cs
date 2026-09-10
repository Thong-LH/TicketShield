using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

public class User : BaseEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? IdCardNumber { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleId { get; set; }
    public string? PasswordResetOtp { get; set; }
    public DateTimeOffset? PasswordResetOtpExpiresAt { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<UserBankAccount> BankAccounts { get; set; } = new List<UserBankAccount>();
    public ICollection<ResaleListing> ResaleListings { get; set; } = new List<ResaleListing>();
    public ICollection<EscrowTransaction> PurchasedEscrows { get; set; } = new List<EscrowTransaction>();
    public ICollection<Dispute> Disputes { get; set; } = new List<Dispute>();
}
