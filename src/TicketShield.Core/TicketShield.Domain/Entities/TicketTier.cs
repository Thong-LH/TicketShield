using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class TicketTier : BaseEntity
{
    public Guid EventId { get; set; }
    public string TierName { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public string? Description { get; set; }

    // Navigation
    public Event Event { get; set; } = null!;
    public ICollection<ResaleListing> ResaleListings { get; set; } = new List<ResaleListing>();
}
