namespace MockOrganizer.API.Gate;

public static class GateScanResults
{
    public const string Success = "SUCCESS";
    public const string Duplicate = "DUPLICATE_ENTRY";
    public const string Invalid = "INVALID_TICKET";
    public const string Revoked = "REVOKED";
}

public sealed class GateScanRequest
{
    public string? ScanId { get; set; }
    public string? TicketCode { get; set; }
    public string? GateName { get; set; }
    public string? ScannerDeviceId { get; set; }
}

public sealed class GateScanResponse
{
    public string ScanId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string TicketCode { get; set; } = string.Empty;
    public string? EventName { get; set; }
    public string? SeatZone { get; set; }
    public DateTimeOffset ScannedAt { get; set; }
    public DateTimeOffset? FirstScannedAt { get; set; }
    public string? FirstGateName { get; set; }
}
