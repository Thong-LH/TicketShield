namespace TicketShield.Application.Features.Admin.Analytics.Models;

/// <summary>
/// DTO phản hồi gói tổng hợp số liệu Doanh thu sàn, Phân tích Người dùng, Kho vé và Giao dịch theo sự kiện
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
public class AdminPlatformAnalyticsResponseDto
{
    public PlatformRevenueStatsDto Revenue { get; set; } = new();
    public UserAnalyticsDto Users { get; set; } = new();
    public TicketInventoryAnalyticsDto Inventory { get; set; } = new();
    public List<EventTransactionAnalyticsDto> EventTransactions { get; set; } = new();
}

