using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class DisputeMessage : BaseEntity
{
    public Guid DisputeId { get; set; }
    public Guid SenderId { get; set; }
    public string SenderRole { get; set; } = "BUYER"; // BUYER, SELLER, ADMIN, CSKH
    public string MessageType { get; set; } = "TEXT";
    public string Content { get; set; } = string.Empty;

    // Navigation
    public Dispute Dispute { get; set; } = null!;
    public User Sender { get; set; } = null!;
}
