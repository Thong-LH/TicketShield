namespace TicketShield.Application.Features.Admin.EscrowBuffer.Models;

public class EscrowBufferConfig
{
    public const int DefaultBufferSeconds = 86400;
    public const int DefaultCutoffSeconds = 7200;

    public int BufferSeconds { get; set; } = DefaultBufferSeconds;
    public int CutoffSeconds { get; set; } = DefaultCutoffSeconds;
}
