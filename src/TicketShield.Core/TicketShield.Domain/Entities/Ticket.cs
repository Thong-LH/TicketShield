using TicketShield.Domain.Common;
using TicketShield.Domain.Enums;

namespace TicketShield.Domain.Entities;

public class Ticket : BaseEntity
{
    public Guid EventId { get; set; }
    public Guid TierId { get; set; }
    public Guid CurrentOwnerId { get; set; }

    public string TicketCode { get; set; } = string.Empty;
    public string? SeatZone { get; set; }
    public decimal OriginalPrice { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Valid;

    public Guid? PreviousTicketId { get; set; }

    // Navigation
    public Event Event { get; set; } = null!;
    public TicketTier Tier { get; set; } = null!;
    public ShadowUser CurrentOwner { get; set; } = null!;
    public Ticket? PreviousTicket { get; set; }
    public ICollection<ResaleListing> ResaleListings { get; set; } = new List<ResaleListing>();
}
