using System.Linq.Expressions;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Common.Helpers;

/// <summary>
/// Groups escrow statuses by where the buyer's money sits, so the Admin Transactions page and the
/// Admin Dashboard count money the same way and Inflow - Payout - Refunded - Held equals Net revenue.
/// Written as explicit comparisons so EF translates them on the string-converted status column.
/// </summary>
public static class EscrowMoneyBuckets
{
    /// <summary>
    /// The buyer has paid and the platform still holds the money: Locked (hold window), Releasing
    /// (payout in flight), Disputed, RefundQueued (refund not sent yet) or TransferNeedsReview.
    /// </summary>
    public static readonly Expression<Func<EscrowTransaction, bool>> IsHeld = e =>
        e.Status == EscrowStatus.Locked ||
        e.Status == EscrowStatus.Releasing ||
        e.Status == EscrowStatus.Disputed ||
        e.Status == EscrowStatus.RefundQueued ||
        e.Status == EscrowStatus.TransferNeedsReview;

    /// <summary>
    /// The buyer's money actually reached the platform: every held status plus Released (paid out to
    /// the seller) and Refunded. Pending, Expired and Cancelled never received money.
    /// </summary>
    public static readonly Expression<Func<EscrowTransaction, bool>> IsInflow = e =>
        e.Status == EscrowStatus.Locked ||
        e.Status == EscrowStatus.Releasing ||
        e.Status == EscrowStatus.Disputed ||
        e.Status == EscrowStatus.RefundQueued ||
        e.Status == EscrowStatus.TransferNeedsReview ||
        e.Status == EscrowStatus.Released ||
        e.Status == EscrowStatus.Refunded;

    /// <summary>
    /// The seller payout for this escrow went through. Paid out is summed from these payout rows,
    /// not from the escrow status.
    /// </summary>
    public static readonly Expression<Func<EscrowTransaction, bool>> HasSuccessfulPayout = e =>
        e.PayoutTransaction != null && e.PayoutTransaction.Status == PayoutStatus.Success;
}
