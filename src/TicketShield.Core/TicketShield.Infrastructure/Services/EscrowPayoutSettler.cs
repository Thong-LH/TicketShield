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
    private readonly TimeSpan _firstRetryDelay;

    public EscrowPayoutSettler(
        ITicketShieldDbContext db,
        IEscrowSettlementCas cas,
        IPayoutGateway gateway)
        : this(db, cas, gateway, TimeSpan.FromSeconds(1))
    {
    }

    public EscrowPayoutSettler(
        ITicketShieldDbContext db,
        IEscrowSettlementCas cas,
        IPayoutGateway gateway,
        TimeSpan firstRetryDelay)
    {
        _db = db;
        _cas = cas;
        _gateway = gateway;
        _firstRetryDelay = firstRetryDelay;
    }

    public async Task<bool> TrySettleAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        var preview = await _db.EscrowTransactions
            .AsNoTracking()
            .Include(row => row.Seller)
            .SingleOrDefaultAsync(row => row.Id == escrowId, cancellationToken);
        if (preview?.Seller == null || !PayoutAccountRules.IsPresent(preview.Seller.PayoutAccountNumber))
        {
            return false;
        }

        var accountNumber = preview.Seller.PayoutAccountNumber.Trim();
        var won = await _cas.TryBeginReleaseAsync(escrowId, cancellationToken);
        if (!won)
        {
            return false;
        }

        return await TransferAfterClaimAsync(preview, accountNumber, preview.RetryCount, cancellationToken);
    }

    public async Task<bool> TryResumeReleaseAsync(Guid escrowId, CancellationToken cancellationToken = default)
    {
        var preview = await _db.EscrowTransactions
            .AsNoTracking()
            .Include(row => row.Seller)
            .SingleOrDefaultAsync(row => row.Id == escrowId, cancellationToken);
        if (preview?.Seller == null || preview.Status != EscrowStatus.Releasing)
        {
            return false;
        }

        var payout = await _db.PayoutTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.EscrowId == escrowId, cancellationToken);
        if (payout?.Status == PayoutStatus.Failed)
        {
            return false;
        }

        if (payout?.Status == PayoutStatus.Success)
        {
            await SaveAttemptAsync(
                preview,
                payout.RetryCount,
                PayoutStatus.Success,
                payout.BankReferenceCode,
                error: null,
                release: true,
                cancellationToken);
            return true;
        }

        var retryCount = payout?.RetryCount ?? preview.RetryCount;
        if (retryCount >= 3)
        {
            return false;
        }

        if (!PayoutAccountRules.IsPresent(preview.Seller.PayoutAccountNumber))
        {
            return false;
        }

        var accountNumber = preview.Seller.PayoutAccountNumber.Trim();
        return await TransferAfterClaimAsync(preview, accountNumber, retryCount, cancellationToken);
    }

    private async Task<bool> TransferAfterClaimAsync(
        EscrowTransaction preview,
        string accountNumber,
        int retryCount,
        CancellationToken cancellationToken)
    {
        if (!PayoutAccountRules.IsValidAccountNumber(accountNumber))
        {
            await SaveAttemptAsync(
                preview,
                retryCount,
                PayoutStatus.Failed,
                bankReference: null,
                error: "STK không hợp lệ",
                release: false,
                cancellationToken);
            return false;
        }

        while (true)
        {
            var transfer = await _gateway.TransferAsync(new PayoutTransferRequest
            {
                EscrowId = preview.Id,
                RetryCount = retryCount,
                BankCode = preview.Seller.PayoutBankCode,
                AccountNumber = accountNumber,
                AccountName = preview.Seller.PayoutAccountName,
                Amount = preview.NetSellerPayout
            }, cancellationToken);

            if (transfer.Outcome == PayoutGatewayOutcome.Succeeded)
            {
                await SaveAttemptAsync(
                    preview,
                    retryCount,
                    PayoutStatus.Success,
                    transfer.BankReferenceCode,
                    error: null,
                    release: true,
                    cancellationToken);
                return true;
            }

            if (transfer.Outcome == PayoutGatewayOutcome.InvalidAccount)
            {
                await SaveAttemptAsync(
                    preview,
                    retryCount,
                    PayoutStatus.Failed,
                    bankReference: null,
                    error: "STK không hợp lệ",
                    release: false,
                    cancellationToken);
                return false;
            }

            retryCount++;
            if (retryCount >= 3)
            {
                await SaveAttemptAsync(
                    preview,
                    retryCount,
                    PayoutStatus.Processing,
                    bankReference: null,
                    error: "Ngân hàng timeout",
                    release: false,
                    cancellationToken);
                return false;
            }

            await SaveAttemptAsync(
                preview,
                retryCount,
                PayoutStatus.Processing,
                bankReference: null,
                error: "Ngân hàng timeout",
                release: false,
                cancellationToken);

            if (_firstRetryDelay > TimeSpan.Zero)
            {
                await Task.Delay(_firstRetryDelay * retryCount, cancellationToken);
            }
        }
    }

    private async Task SaveAttemptAsync(
        EscrowTransaction preview,
        int retryCount,
        PayoutStatus status,
        string? bankReference,
        string? error,
        bool release,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var payout = await _db.PayoutTransactions
            .FirstOrDefaultAsync(row => row.EscrowId == preview.Id, cancellationToken);
        if (payout == null)
        {
            payout = new PayoutTransaction
            {
                Id = Guid.NewGuid(),
                EscrowId = preview.Id,
                SellerId = preview.SellerId,
                PayoutCode = $"PO-{preview.Id.ToString("N")[..8].ToUpperInvariant()}",
                CreatedAt = now
            };
            _db.PayoutTransactions.Add(payout);
        }

        payout.RecipientBankCode = preview.Seller.PayoutBankCode;
        payout.RecipientAccountNumber = preview.Seller.PayoutAccountNumber;
        payout.RecipientAccountName = preview.Seller.PayoutAccountName;
        payout.Amount = preview.NetSellerPayout;
        payout.Status = status;
        payout.BankReferenceCode = bankReference;
        payout.RetryCount = retryCount;
        payout.LastErrorMessage = error;
        payout.ProcessedAt = status == PayoutStatus.Success ? now : null;
        payout.UpdatedAt = now;

        var tracked = await _db.EscrowTransactions.SingleAsync(row => row.Id == preview.Id, cancellationToken);
        tracked.RetryCount = retryCount;
        tracked.UpdatedAt = now;
        if (release)
        {
            tracked.Status = EscrowStatus.Released;
            tracked.InSettlementBuffer = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
