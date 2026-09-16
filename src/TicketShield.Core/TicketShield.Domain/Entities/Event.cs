using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class Event : BaseEntity
{
    public Guid OrganizerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Venue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }
    public DateTimeOffset EventEndAt { get; set; }
    public DateTimeOffset ResaleDeadline { get; set; }
    public string Status { get; set; } = "UPCOMING";

    /// <summary>
    /// Extra percent above face value that a resale may charge (0 = old 100% ceiling). Admin-owned.
    /// </summary>
    public decimal MaxResaleMarkupPercentage { get; set; }

    // Navigation
    public Organizer Organizer { get; set; } = null!;
    public ICollection<TicketTier> TicketTiers { get; set; } = new List<TicketTier>();
    public ICollection<ResaleListing> ResaleListings { get; set; } = new List<ResaleListing>();
}
