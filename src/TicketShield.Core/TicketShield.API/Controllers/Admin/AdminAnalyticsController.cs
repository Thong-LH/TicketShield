using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Analytics.Models;
using TicketShield.Application.Features.Admin.Analytics.Queries.GetAdminPlatformAnalytics;

namespace TicketShield.API.Controllers.Admin;

/// <summary>
/// Quản trị và phân tích tổng quan sàn (Platform Revenue and Ticket Inventory Analytics)
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-338 (BE-11.1.5)
/// </summary>
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/analytics")]
public class AdminAnalyticsController : ApiControllerBase
{
    /// <summary>
    /// Thống kê tổng doanh thu thực tế của sàn, tổng số người dùng (Buyer vs Seller), số lượng vé giao dịch theo từng sự kiện và kho vé Admin.
    /// </summary>
    /// <param name="query">Tham số lọc theo khoảng thời gian FromDate / ToDate.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <returns>Dữ liệu phân tích doanh thu, cơ cấu người dùng, trạng thái kho vé và giao dịch chi tiết theo sự kiện.</returns>
    [HttpGet("platform-revenue-inventory")]
    [ProducesResponseType(typeof(ApiResponse<AdminPlatformAnalyticsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPlatformRevenueAndInventoryAnalytics(
        [FromQuery] GetAdminPlatformAnalyticsQuery query,
        CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }
}
