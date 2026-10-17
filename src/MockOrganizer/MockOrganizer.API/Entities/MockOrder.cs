namespace MockOrganizer.API.Entities;

public class MockOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TierId { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string TierName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "SUCCESS";    // SUCCESS, PENDING, CONFIRMED, CANCELLED
    public string? BuyerEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
