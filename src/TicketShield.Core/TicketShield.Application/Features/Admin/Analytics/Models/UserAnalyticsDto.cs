namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// Thống kê phân loại cấu trúc người dùng (Buyer vs Seller)
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class UserAnalyticsDto
{
    /// <summary>
    /// Tổng số tài khoản người dùng đăng ký trên hệ thống.
    /// </summary>
    public int TotalUsers { get; set; }

    /// <summary>
    /// Tổng số người dùng đã từng mua vé (Unique Buyer).
    /// </summary>
    public int TotalBuyers { get; set; }

    /// <summary>
    /// Tổng số người dùng đã từng đăng bán vé (Unique Seller).
    /// </summary>
    public int TotalSellers { get; set; }

    /// <summary>
    /// Số người dùng tham gia cả 2 vai trò (vừa mua vừa bán).
    /// </summary>
    public int DualRoleUsers { get; set; }

    /// <summary>
    /// Số tài khoản đang hoạt động bình thường (IsActive == true).
    /// </summary>
    public int ActiveUsers { get; set; }
}

