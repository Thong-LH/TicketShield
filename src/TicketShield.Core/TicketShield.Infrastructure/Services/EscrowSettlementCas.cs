using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class EscrowSettlementCas : IEscrowSettlementCas
{
    private readonly ITicketShieldDbContext _db;

    public EscrowSettlementCas(ITicketShieldDbContext db)
    {
        _db = db;
    }

    public async Task<bool> TryBeginReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var won = await TryTransitionFromLockedAsync(escrowId, EscrowStatus.Releasing, cancellationToken);
        if (!won)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var escrow = await _db.EscrowTransactions.AsNoTracking()
            .SingleAsync(row => row.Id == escrowId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var payoutRequested = new PayoutRequestedEvent
        {
            EscrowId = escrow.Id,
            SellerId = escrow.SellerId,
            Amount = escrow.NetSellerPayout,
            RetryCount = escrow.RetryCount,
            IdempotencyKey = PayoutIdempotency.Key(escrow.Id, escrow.RetryCount)
        };
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(PayoutRequestedEvent),
            Payload = JsonSerializer.Serialize(payoutRequested),
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

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
