namespace TicketShield.Application.Common.Models;

public class PayoutTransferRequest
{
    public Guid EscrowId { get; set; }
    public int RetryCount { get; set; }
    public string BankCode { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class PayoutGatewayResult
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public string BankReferenceCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyTransferred { get; set; }
}
