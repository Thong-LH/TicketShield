namespace TicketShield.Application.Features.Payouts.Queries.GetMyPayouts;

public class MyPayoutDto
{
    public Guid PayoutId { get; set; }
    public string PayoutCode { get; set; } = string.Empty;
    public Guid EscrowId { get; set; }
    public string EscrowStatus { get; set; } = string.Empty;
    public DateTimeOffset? UnlockAt { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string OriginalTicketCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string RecipientBankCode { get; set; } = string.Empty;
    public string RecipientAccountNumber { get; set; } = string.Empty;
    public string RecipientAccountName { get; set; } = string.Empty;
    public string? BankReferenceCode { get; set; }
    public int RetryCount { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
