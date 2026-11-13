using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Orders.Models;
using TicketShield.Application.Features.Orders.Queries.GetBuyerOrderHistory;

namespace TicketShield.API.Controllers;

/// <summary>
/// Quản lý lịch sử đơn hàng và sổ cái tài chính của người mua (Buyer Order History and Financial Ledger)
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
[Authorize]
[Route("api/v1/orders")]
public class OrdersController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách các giao dịch mua vé của User (Mã đơn, tên sự kiện, số tiền đã chuyển khoản qua VietQR, mã tham chiếu SePay, trạng thái hoàn tiền).
    /// </summary>
    /// <param name="query">Tham số lọc theo trạng thái, từ khóa tìm kiếm, khoảng ngày và phân trang.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <returns>Danh sách đơn hàng phân trang kèm tóm tắt tài chính cá nhân của Buyer.</returns>
    [HttpGet("my-orders")]
    [HttpGet("/api/v1/buyer/orders")]
    [ProducesResponseType(typeof(ApiResponse<BuyerOrderHistoryResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyOrders(
        [FromQuery] GetBuyerOrderHistoryQuery query,
        CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }
}
