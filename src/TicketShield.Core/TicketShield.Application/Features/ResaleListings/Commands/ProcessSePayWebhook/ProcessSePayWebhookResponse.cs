namespace TicketShield.Application.Features.ResaleListings.Commands.ProcessSePayWebhook;

public class ProcessSePayWebhookResponse
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string EscrowStatus { get; set; } = string.Empty;
    public string ListingStatus { get; set; } = string.Empty;
    public decimal TransferAmount { get; set; }
    public string? BankTransactionReference { get; set; }
    public bool IsIdempotentDuplicate { get; set; }
}
