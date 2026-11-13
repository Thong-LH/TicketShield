namespace TicketShield.Application.Features.Orders.Models;

/// <summary>
/// Thống kê tổng hợp số lượng đơn hàng và dòng tiền của người mua (Buyer)
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
public class BuyerOrderSummaryDto
{
    /// <summary>
    /// Tổng số đơn hàng đã đặt mua.
    /// </summary>
    public int TotalOrders { get; set; }

    /// <summary>
    /// Tổng số tiền đã thanh toán chuyển khoản thành công (Tổng tiền của các đơn Released và Locked).
    /// </summary>
    public decimal TotalSpent { get; set; }

    /// <summary>
    /// Tổng số tiền đã nhận hoàn tiền thành công (Status == Refunded).
    /// </summary>
    public decimal TotalRefunded { get; set; }

    /// <summary>
    /// Số lượng đơn đang trong thời gian bảo vệ ký quỹ an toàn (Status == Locked).
    /// </summary>
    public int InEscrowOrdersCount { get; set; }
}

