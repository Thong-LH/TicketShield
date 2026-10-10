namespace TicketShield.Application.Features.Disputes;

public sealed class GateScan
{
    public string TicketCode { get; set; } = string.Empty;
    public DateTimeOffset ScannedAt { get; set; }
    public string ScanResult { get; set; } = string.Empty;
    public string GateName { get; set; } = string.Empty;
    public string? ScannerDeviceId { get; set; }
}
