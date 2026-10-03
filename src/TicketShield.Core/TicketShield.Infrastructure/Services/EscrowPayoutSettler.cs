using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class EscrowPayoutSettler : IEscrowPayoutSettler
{
    private readonly ITicketShieldDbContext _db;
    private readonly IEscrowSettlementCas _cas;
    private readonly IPayoutGateway _gateway;

    public EscrowPayoutSettler(
        ITicketShieldDbContext db,
        IEscrowSettlementCas cas,
        IPayoutGateway gateway)
    {
        _db = db;
        _cas = cas;
        _gateway = gateway;
    }

    public async Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        var won = await _cas.TryBeginReleaseAsync(escrowId, cancellationToken);
        if (!won)
        {
            return false;
        }

        var escrow = await _db.EscrowTransactions
            .AsNoTracking()
            .Include(row => row.Seller)
            .SingleAsync(row => row.Id == escrowId, cancellationToken);

        var transfer = await _gateway.TransferAsync(new PayoutTransferRequest
        {
            EscrowId = escrow.Id,
            RetryCount = escrow.RetryCount,
            AccountName = escrow.Seller?.FullName ?? string.Empty,
            Amount = escrow.NetSellerPayout
        }, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var payout = await _db.PayoutTransactions
            .FirstOrDefaultAsync(row => row.EscrowId == escrowId, cancellationToken);
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

        payout.RecipientAccountName = string.IsNullOrWhiteSpace(escrow.Seller?.FullName)
            ? string.Empty
            : escrow.Seller.FullName.ToUpperInvariant();
        payout.Amount = transfer.Amount;
        payout.Status = PayoutStatus.Success;
        payout.BankReferenceCode = transfer.BankReferenceCode;
        payout.RetryCount = escrow.RetryCount;
        payout.ProcessedAt = now;
        payout.UpdatedAt = now;

        var tracked = await _db.EscrowTransactions.SingleAsync(row => row.Id == escrowId, cancellationToken);
        tracked.Status = EscrowStatus.Released;
        tracked.InSettlementBuffer = false;
        tracked.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
