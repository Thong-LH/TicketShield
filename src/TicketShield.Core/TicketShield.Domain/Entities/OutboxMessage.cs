using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class OutboxMessage : BaseEntity
{
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset? ProcessedAt { get; set; }
}
