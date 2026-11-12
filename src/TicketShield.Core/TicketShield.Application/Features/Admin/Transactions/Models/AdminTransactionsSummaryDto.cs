namespace TicketShield.Application.Features.Admin.Transactions.Models;

/// <summary>
/// Thống kê tổng hợp dòng tiền Ký quỹ và Giải ngân phục vụ Admin Dashboard / KPI Cards
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-334 (BE-11.1.1)
/// </summary>
public class AdminTransactionsSummaryDto
{
    /// <summary>
    /// Tổng số tiền cọc/ký quỹ đang bị giam giữ an toàn (Status = Locked hoặc InSettlementBuffer = true).
    /// </summary>
    public decimal TotalEscrowLockedAmount { get; set; }

    /// <summary>
    /// Số lượng giao dịch đang bị khóa ký quỹ chờ unlock.
    /// </summary>
    public int TotalLockedCount { get; set; }

    /// <summary>
    /// Tổng số tiền đã giải ngân thành công cho người bán (Status = Released hoặc PayoutStatus = Success).
    /// </summary>
    public decimal TotalPayoutCompletedAmount { get; set; }

    /// <summary>
    /// Tổng số tiền đã hoàn trả cho người mua (Status = Refunded).
    /// </summary>
    public decimal TotalRefundedAmount { get; set; }

    /// <summary>
    /// Số lượng giao dịch đang có khiếu nại/tranh chấp.
    /// </summary>
    public int TotalDisputedCount { get; set; }
}

