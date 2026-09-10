namespace MockOrganizer.API.Entities;

public class MockTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketCode { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string SeatZone { get; set; } = string.Empty;
    public decimal OriginalPrice { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string? OwnerPhone { get; set; }
    public string? OwnerName { get; set; }
    public string Status { get; set; } = "VALID"; // VALID, LOCKED_FOR_RESALE, TRANSFERRED, USED, CANCELLED
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
