using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class EscrowSettlementCas : IEscrowSettlementCas
{
    private readonly ITicketShieldDbContext _db;

    public EscrowSettlementCas(ITicketShieldDbContext db)
    {
        _db = db;
    }

    public Task<bool> TryBeginReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
        => TryTransitionFromLockedAsync(escrowId, EscrowStatus.Releasing, cancellationToken);

    public Task<bool> TryBeginDisputeAsync(Guid escrowId, CancellationToken cancellationToken = default)
        => TryTransitionFromLockedAsync(escrowId, EscrowStatus.Disputed, cancellationToken);

    private async Task<bool> TryTransitionFromLockedAsync(
        Guid escrowId,
        EscrowStatus nextStatus,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var updated = await _db.EscrowTransactions
            .Where(escrow => escrow.Id == escrowId && escrow.Status == EscrowStatus.Locked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(escrow => escrow.Status, nextStatus)
                    .SetProperty(escrow => escrow.UpdatedAt, updatedAt),
                cancellationToken);

        return updated == 1;
    }
}
