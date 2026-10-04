using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Services;

public class EscrowPayoutSettler : IEscrowPayoutSettler
{
    private readonly ITicketShieldDbContext _db;
    private readonly IEscrowSettlementCas _cas;

    public EscrowPayoutSettler(ITicketShieldDbContext db, IEscrowSettlementCas cas)
    {
        _db = db;
        _cas = cas;
    }

    public async Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        var sellerAccount = await _db.EscrowTransactions
            .AsNoTracking()
            .Where(row => row.Id == escrowId)
            .Select(row => row.Seller.PayoutAccountNumber)
            .SingleOrDefaultAsync(cancellationToken);
        if (!PayoutAccountRules.IsPresent(sellerAccount))
        {
            return false;
        }

        return await _cas.TryBeginReleaseAsync(escrowId, cancellationToken);
    }

    public Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
        => Task.FromResult(false);
}
