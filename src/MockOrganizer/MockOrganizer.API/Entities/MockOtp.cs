namespace MockOrganizer.API.Entities;

public class MockOtp
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketCode { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
    public int Attempts { get; set; } = 0;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
