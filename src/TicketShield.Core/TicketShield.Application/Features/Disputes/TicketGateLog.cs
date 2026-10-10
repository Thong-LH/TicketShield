namespace TicketShield.Application.Features.Disputes;

public sealed class TicketGateLog
{
    public required string TicketCode { get; init; }
    public required IReadOnlyList<GateScan> Scans { get; init; }
}
