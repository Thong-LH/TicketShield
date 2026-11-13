namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// Thống kê số lượng vé giao dịch, giá trị thanh toán và doanh thu sàn theo từng sự kiện
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class EventTransactionAnalyticsDto
{
    public Guid EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public DateTimeOffset EventStartAt { get; set; }

    /// <summary>
    /// Tổng số tin đăng bán cho sự kiện này.
    /// </summary>
    public int TotalListings { get; set; }

    /// <summary>
    /// Số lượng vé đã giao dịch thành công (Sold / Released).
    /// </summary>
    public int TotalTicketsSold { get; set; }

    /// <summary>
    /// Số lượng vé đang mở bán hợp lệ (Verified).
    /// </summary>
    public int TotalTicketsAvailable { get; set; }

    /// <summary>
    /// Tổng giá trị giao dịch của sự kiện (GMV = Tổng tiền người mua trả).
    /// </summary>
    public decimal TotalTransactionVolume { get; set; }

    /// <summary>
    /// Tổng doanh thu sàn thu được từ sự kiện (BuyerFee + SellerFee).
    /// </summary>
    public decimal TotalPlatformRevenue { get; set; }
}

