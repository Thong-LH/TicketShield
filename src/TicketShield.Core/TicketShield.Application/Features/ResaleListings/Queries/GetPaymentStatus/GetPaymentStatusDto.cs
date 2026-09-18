namespace TicketShield.Application.Features.ResaleListings.Queries.GetPaymentStatus;

public sealed class GetPaymentStatusDto
{
    public Guid ListingId { get; set; }
    public Guid EscrowId { get; set; }
    public string ListingStatus { get; set; } = string.Empty;
    public string EscrowStatus { get; set; } = string.Empty;
    public string? PaymentReference { get; set; }
    public DateTimeOffset? UnlockAt { get; set; }
    public bool InSettlementBuffer { get; set; }
}
