namespace TicketShield.Application.Features.Admin.Transactions.Models;

/// <summary>
/// Summary cards of the Admin Transactions page (SCRUM-333 / SCRUM-334, SCRUM-390 / SCRUM-394).
/// Every number is computed over the rows that match the current filters, across all pages.
/// Money buckets follow <c>EscrowMoneyBuckets</c> so Inflow - Paid out - Refunded - Held equals Net revenue.
/// </summary>
public class AdminTransactionsSummaryDto
{
    /// <summary>
    /// Inflow: total paid by buyers on escrows whose money reached the platform
    /// (held statuses plus Released and Refunded).
    /// </summary>
    public decimal TotalInflowAmount { get; set; }

    /// <summary>
    /// Net revenue: buyer fee plus seller fee of the Released escrows that match the filters.
    /// Same formula as <c>PlatformRevenueStatsDto.TotalNetRevenue</c> on the Admin Dashboard.
    /// Summed from the fees, not from Inflow - Paid out - Refunded - Held, so a Released escrow
    /// without a Success payout adds only its fees.
    /// </summary>
    public decimal TotalNetRevenue { get; set; }

    /// <summary>
    /// Held in escrow: total paid by buyers on escrows the platform still holds
    /// (Locked, Releasing, Disputed, RefundQueued, TransferNeedsReview).
    /// Name kept from the all-time version so existing clients do not break.
    /// </summary>
    public decimal TotalEscrowLockedAmount { get; set; }

    /// <summary>
    /// Number of escrows counted in <see cref="TotalEscrowLockedAmount"/>.
    /// </summary>
    public int TotalLockedCount { get; set; }

    /// <summary>
    /// Paid out: total amount of seller payouts whose status is Success.
    /// </summary>
    public decimal TotalPayoutCompletedAmount { get; set; }

    /// <summary>
    /// Refunded: total paid by buyers on escrows with status Refunded.
    /// </summary>
    public decimal TotalRefundedAmount { get; set; }

    /// <summary>
    /// In dispute: number of escrows whose status is Disputed right now. Resolved disputes are not counted.
    /// </summary>
    public int TotalDisputedCount { get; set; }
}
