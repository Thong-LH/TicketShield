namespace TicketShield.Application.Common.Models;

public class PayoutRequestedEvent
{
    public Guid EscrowId { get; set; }
    public Guid SellerId { get; set; }
    public decimal Amount { get; set; }
    public int RetryCount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class PayoutIdempotency
{
    public static string Key(Guid escrowId, int retryCount) => $"IDEMP-{escrowId}-{retryCount}";
}
