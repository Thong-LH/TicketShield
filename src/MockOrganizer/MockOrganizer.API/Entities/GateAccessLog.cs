namespace MockOrganizer.API.Entities;

public class GateAccessLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TicketCode { get; set; } = string.Empty;
    public string GateName { get; set; } = string.Empty;
    public DateTimeOffset ScannedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ScanResult { get; set; } = "SUCCESS"; // SUCCESS, DUPLICATE_ENTRY, INVALID_TICKET, REVOKED
    public string? ScannerDeviceId { get; set; }
    public string? Notes { get; set; }
}
