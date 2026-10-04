namespace TicketShield.Application.Common.Interfaces;

public interface IEscrowPayoutSettler
{
    Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default);

    Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default);
}
