using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Common.Models;

public static class PayoutReleaseLetter
{
    public static PayoutRequestedEvent Create(EscrowTransaction escrow, ShadowUser seller)
    {
        return new PayoutRequestedEvent
        {
            EscrowId = escrow.Id,
            SellerId = escrow.SellerId,
            Amount = escrow.NetSellerPayout,
            RetryCount = escrow.RetryCount,
            IdempotencyKey = PayoutIdempotency.Key(escrow.Id, escrow.RetryCount),
            BankCode = seller.PayoutBankCode,
            AccountNumber = seller.PayoutAccountNumber.Trim(),
            AccountName = seller.PayoutAccountName
        };
    }

    public static string Code(Guid escrowId) => $"PO-{escrowId.ToString("N")[..8].ToUpperInvariant()}";

    public static void Apply(PayoutTransaction payout, PayoutRequestedEvent letter, DateTimeOffset now)
    {
        payout.RecipientBankCode = letter.BankCode;
        payout.RecipientAccountNumber = letter.AccountNumber;
        payout.RecipientAccountName = letter.AccountName;
        payout.Amount = letter.Amount;
        payout.Status = PayoutStatus.Processing;
        payout.RetryCount = letter.RetryCount;
        payout.UpdatedAt = now;
    }
}
