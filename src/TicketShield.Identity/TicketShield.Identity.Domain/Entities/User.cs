using TicketShield.Identity.Domain.Common;
using TicketShield.Identity.Domain.Enums;

namespace TicketShield.Identity.Domain.Entities;

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
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
