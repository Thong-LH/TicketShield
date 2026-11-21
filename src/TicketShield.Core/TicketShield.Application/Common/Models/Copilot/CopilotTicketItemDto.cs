namespace TicketShield.Application.Common.Models.Copilot;

public class CopilotTicketItemDto
{
    public Guid ListingId { get; set; }
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public string? SeatZone { get; set; }
    public decimal ResalePrice { get; set; }
    public decimal OriginalPrice { get; set; }
    public decimal DiscountPercentage { get; set; }
    public DateTimeOffset EventStartAt { get; set; }
    public string Venue { get; set; } = string.Empty;
}
