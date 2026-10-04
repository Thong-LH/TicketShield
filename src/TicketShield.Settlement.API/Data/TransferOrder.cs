namespace TicketShield.Settlement.API.Data;

public class TransferOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid EscrowId { get; set; }
    public Guid SellerId { get; set; }
    public int RetryNumber { get; set; }
    public decimal Amount { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string? BankReference { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public TransferState State { get; set; } = TransferState.Pending;
    public DateTimeOffset? ReportDeliveredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum TransferState
{
    Pending,
    Succeeded,
    Failed,
    Exhausted
}
