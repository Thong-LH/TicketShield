namespace MockOrganizer.API.Entities;

public class TicketTier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = string.Empty;      // slug, e.g. "atsh-2026"
    public string TierName { get; set; } = string.Empty;     // "VIP Zone A", "GA Standing"
    public decimal Price { get; set; }
    public int TotalQuantity { get; set; }
    public int AvailableQuantity { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
