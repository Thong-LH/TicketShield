namespace TicketShield.Application.Common.Interfaces;

public interface IEscrowSettlementCas
{
    Task<bool> TryBeginReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default);

    Task<bool> TryBeginDisputeAsync(Guid escrowId, CancellationToken cancellationToken = default);
}
