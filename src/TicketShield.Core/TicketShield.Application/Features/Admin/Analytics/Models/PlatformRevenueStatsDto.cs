namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// Thống kê tổng doanh thu thực tế và dòng tiền phí dịch vụ của sàn
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class PlatformRevenueStatsDto
{
    /// <summary>
    /// Tổng doanh thu ròng sàn thu được (BuyerFee + SellerFee từ các đơn giao dịch hoàn tất Released).
    /// </summary>
    public decimal TotalNetRevenue { get; set; }

    /// <summary>
    /// Doanh thu từ phí dịch vụ người mua (Buyer Fee).
    /// </summary>
    public decimal TotalBuyerFees { get; set; }

    /// <summary>
    /// Doanh thu từ phí dịch vụ người bán (Seller Fee).
    /// </summary>
    public decimal TotalSellerFees { get; set; }

    /// <summary>
    /// Tổng giá trị giao dịch thành công toàn sàn (Gross Merchandise Value - GMV).
    /// </summary>
    public decimal TotalGmv { get; set; }

    /// <summary>
    /// Doanh thu phí tiềm năng đang bị tạm khóa trong Escrow chờ giải ngân (Status == Locked).
    /// </summary>
    public decimal PendingFeesInEscrow { get; set; }

    /// <summary>
    /// Tổng số tiền đã hoàn trả cho người mua do tranh chấp/hủy lệnh (Status == Refunded).
    /// </summary>
    public decimal TotalRefundedAmount { get; set; }

    /// <summary>
    /// Inflow (SCRUM-392): total paid by buyers on escrows whose money reached the platform.
    /// Same bucket as the Inflow card of the Admin Transactions page.
    /// </summary>
    public decimal TotalInflow { get; set; }

    /// <summary>
    /// Payout (SCRUM-392): total amount of seller payouts whose status is Success.
    /// Same number as the Paid out card of the Admin Transactions page.
    /// </summary>
    public decimal TotalPayout { get; set; }

    /// <summary>
    /// Held in escrow (SCRUM-393): total paid by buyers on escrows the platform still holds.
    /// Inflow - Payout - Refunded - Held in escrow equals Net revenue.
    /// </summary>
    public decimal TotalHeldInEscrow { get; set; }
}

