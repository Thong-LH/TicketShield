namespace TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;

public class UserBankAccountDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string BankAccountNumber { get; set; } = string.Empty;
    public string AccountHolderName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
