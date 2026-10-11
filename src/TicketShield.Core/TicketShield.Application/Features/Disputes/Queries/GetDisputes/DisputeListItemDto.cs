using TicketShield.Application.Features.Disputes;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Disputes.Queries.GetDisputes;

public class DisputeEvidenceDto
{
    public Guid EvidenceId { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class DisputeListItemDto
{
    public Guid DisputeId { get; set; }
    public string DisputeCode { get; set; } = string.Empty;
    public DisputeReasonCode ReasonCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public decimal RefundAmount { get; set; }
    public string? AdminNotes { get; set; }
    public string? Recommendation { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? TransferredAt { get; set; }
    public DateTimeOffset? HarvestedAt { get; set; }
    public Guid EscrowId { get; set; }
    public decimal TotalBuyerPaid { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public Guid BuyerId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerEmail { get; set; } = string.Empty;
    public Guid SellerId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string SellerEmail { get; set; } = string.Empty;
    public List<DisputeEvidenceDto> Evidences { get; set; } = new();
    public List<GateScan> Scans { get; set; } = new();
}
