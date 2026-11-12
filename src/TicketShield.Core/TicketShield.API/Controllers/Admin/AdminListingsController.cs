using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Listings.Models;
using TicketShield.Application.Features.Admin.Listings.Queries.GetAdminListings;

namespace TicketShield.API.Controllers.Admin;

/// <summary>
/// Quản trị tin bán lẻ và gói Combo kèm giám sát trần giá (Price Ceiling Inspection)
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-336 (BE-11.1.3)
/// </summary>
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/listings")]
public class AdminListingsController : ApiControllerBase
{
    /// <summary>
    /// Tra cứu danh sách tin bán lẻ và gói Combo, thông tin người bán, giá niêm yết và trạng thái kiểm soát trần giá.
    /// </summary>
    /// <param name="query">Tham số lọc theo trạng thái, gói combo/vé lẻ, trần giá, sự kiện, người bán, ngày tạo và từ khóa.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <returns>Danh sách tin đăng phân trang kèm thống kê tổng quan.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminListingsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetListings([FromQuery] GetAdminListingsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }
}
