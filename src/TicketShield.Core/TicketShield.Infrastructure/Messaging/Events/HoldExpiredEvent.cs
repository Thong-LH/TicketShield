using TicketShield.Contracts.Events;

namespace TicketShield.Infrastructure.Messaging.Events;

public class HoldExpiredEvent : IHoldExpiredEvent
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
}
