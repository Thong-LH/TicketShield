namespace TicketShield.Application.Features.Admin.Listings.Models;

/// <summary>
/// Thống kê tổng hợp số lượng tin đăng, combo, tin bán chạy và tin chạm trần giá phục vụ Admin Dashboard / KPI Cards
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-336 (BE-11.1.3)
/// </summary>
public class AdminListingsSummaryDto
{
    /// <summary>
    /// Tổng số tin đăng trên sàn.
    /// </summary>
    public int TotalListings { get; set; }

    /// <summary>
    /// Tổng số tin bán lẻ (không thuộc combo).
    /// </summary>
    public int TotalSingleTickets { get; set; }

    /// <summary>
    /// Tổng số tin thuộc gói Combo.
    /// </summary>
    public int TotalBundles { get; set; }

    /// <summary>
    /// Tổng số tin đang mở bán hợp lệ (Verified).
    /// </summary>
    public int TotalActiveListings { get; set; }

    /// <summary>
    /// Tổng số tin đã bán thành công (Sold).
    /// </summary>
    public int TotalSoldListings { get; set; }

    /// <summary>
    /// Tổng số tin đăng niêm yết chạm hoặc sát trần giá tối đa cho phép.
    /// </summary>
    public int TotalAtCeilingListings { get; set; }
}

