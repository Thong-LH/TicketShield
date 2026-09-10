using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

public class Dispute : BaseEntity
{
    public Guid EscrowId { get; set; }
    public Guid BuyerId { get; set; }
    public string DisputeCode { get; set; } = string.Empty;
    public DisputeReasonCode ReasonCode { get; set; } = DisputeReasonCode.TicketInvalid;
    public string Description { get; set; } = string.Empty;
    public DisputeStatus Status { get; set; } = DisputeStatus.Open;
    public DisputeResolution? Resolution { get; set; }
    public decimal RefundAmount { get; set; } = 0;
    public string? AdminNotes { get; set; }
    public Guid? ResolvedBy { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }

    // Navigation
    public EscrowTransaction Escrow { get; set; } = null!;
    public User Buyer { get; set; } = null!;
    public User? Resolver { get; set; }
    public ICollection<DisputeEvidence> Evidences { get; set; } = new List<DisputeEvidence>();
    public ICollection<DisputeMessage> Messages { get; set; } = new List<DisputeMessage>();
}
