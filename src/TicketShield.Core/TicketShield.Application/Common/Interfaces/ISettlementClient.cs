using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Common.Interfaces;

public interface ISettlementClient
{
    Task<bool> SendAsync(PayoutRequestedEvent command, CancellationToken cancellationToken = default);

    Task<SettlementTransferStatus?> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}

public class SettlementTransferStatus
{
    public string State { get; set; } = string.Empty;
    public string? BankReference { get; set; }
    public int AttemptCount { get; set; }
}
