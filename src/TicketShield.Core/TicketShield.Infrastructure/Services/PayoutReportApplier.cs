using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Enums;

namespace TicketShield.Infrastructure.Services;

public class PayoutReportApplier : IPayoutReportApplier
{
    private readonly ITicketShieldDbContext _db;
    private readonly IPayoutRealtimeNotifier? _payoutNotifier;

    public PayoutReportApplier(ITicketShieldDbContext db, IPayoutRealtimeNotifier? payoutNotifier = null)
    {
        _db = db;
        _payoutNotifier = payoutNotifier;
    }

    public async Task<bool> ApplyAsync(Guid escrowId, bool succeeded, string? bankReference, CancellationToken cancellationToken = default)
    {
        var escrow = await _db.EscrowTransactions.SingleOrDefaultAsync(row => row.Id == escrowId, cancellationToken);
        if (escrow == null || escrow.Status != EscrowStatus.Releasing)
        {
            return false;
        }

        var payout = await _db.PayoutTransactions.FirstOrDefaultAsync(row => row.EscrowId == escrowId, cancellationToken);
        if (payout == null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (succeeded)
        {
            if (!string.IsNullOrWhiteSpace(payout.BankReferenceCode))
            {
                return false;
            }

            payout.Status = PayoutStatus.Success;
            payout.BankReferenceCode = bankReference;
            payout.ProcessedAt = now;
            payout.UpdatedAt = now;
            escrow.Status = EscrowStatus.Released;
            escrow.InSettlementBuffer = false;
            escrow.UpdatedAt = now;
            await _db.SaveChangesAsync(cancellationToken);

            if (_payoutNotifier != null)
            {
                var payload = new
                {
                    EscrowId = escrow.Id,
                    ListingId = escrow.ListingId,
                    SellerId = escrow.SellerId,
                    Amount = payout.Amount,
                    NetSellerPayout = escrow.NetSellerPayout,
                    BankCode = payout.RecipientBankCode,
                    AccountNumber = payout.RecipientAccountNumber,
                    AccountName = payout.RecipientAccountName,
                    BankReference = bankReference,
                    Status = "Success",
                    ProcessedAt = now
                };
                await _payoutNotifier.NotifyPayoutCompletedAsync(escrow.SellerId, escrow.Id, escrow.ListingId, payload, cancellationToken);
            }

            return true;
        }

        if (payout.Status == PayoutStatus.Failed)
        {
            return false;
        }

        payout.Status = PayoutStatus.Failed;
        payout.LastErrorMessage = "STK không hợp lệ";
        payout.UpdatedAt = now;
        escrow.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
