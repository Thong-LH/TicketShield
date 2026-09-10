using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class DisputeEvidence : BaseEntity
{
    public Guid DisputeId { get; set; }
    public Guid UploaderId { get; set; }
    public string EvidenceType { get; set; } = "IMAGE"; // IMAGE, VIDEO, GATE_REPORT_DOC
    public string FileUrl { get; set; } = string.Empty;
    public string? Description { get; set; }

    // Navigation
    public Dispute Dispute { get; set; } = null!;
    public User Uploader { get; set; } = null!;
}
