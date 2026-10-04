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
        var escrow = await _db.EscrowTransactions
            .AsNoTracking()
            .Include(row => row.Seller)
            .SingleOrDefaultAsync(row => row.Id == escrowId, cancellationToken);
        if (escrow?.Seller == null || !PayoutAccountRules.IsPresent(escrow.Seller.PayoutAccountNumber))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var won = await TryTransitionFromLockedAsync(escrowId, EscrowStatus.Releasing, cancellationToken);
        if (!won)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var accountNumber = escrow.Seller.PayoutAccountNumber.Trim();
        var payoutRequested = new PayoutRequestedEvent
        {
            EscrowId = escrow.Id,
            SellerId = escrow.SellerId,
            Amount = escrow.NetSellerPayout,
            RetryCount = escrow.RetryCount,
            IdempotencyKey = PayoutIdempotency.Key(escrow.Id, escrow.RetryCount),
            BankCode = escrow.Seller.PayoutBankCode,
            AccountNumber = accountNumber,
            AccountName = escrow.Seller.PayoutAccountName
        };
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = nameof(PayoutRequestedEvent),
            Payload = JsonSerializer.Serialize(payoutRequested),
            CreatedAt = now,
            UpdatedAt = now
        });

        var payout = await _db.PayoutTransactions.FirstOrDefaultAsync(row => row.EscrowId == escrow.Id, cancellationToken);
        if (payout == null)
        {
            payout = new PayoutTransaction
            {
                Id = Guid.NewGuid(),
                EscrowId = escrow.Id,
                SellerId = escrow.SellerId,
                PayoutCode = $"PO-{escrow.Id.ToString("N")[..8].ToUpperInvariant()}",
                CreatedAt = now
            };
            _db.PayoutTransactions.Add(payout);
        }

        payout.RecipientBankCode = payoutRequested.BankCode;
        payout.RecipientAccountNumber = accountNumber;
        payout.RecipientAccountName = payoutRequested.AccountName;
        payout.Amount = escrow.NetSellerPayout;
        payout.Status = PayoutStatus.Processing;
        payout.RetryCount = escrow.RetryCount;
        payout.UpdatedAt = now;
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
