using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Transactions.Models;
using TicketShield.Application.Features.Admin.Transactions.Queries.GetAdminTransactions;

namespace TicketShield.API.Controllers.Admin;

/// <summary>
/// Quản trị giao dịch Ký quỹ (Escrow) và Sổ cái giải ngân (Payout Ledger)
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-334 (BE-11.1.1)
/// </summary>
[Authorize(Roles = "Admin")]
[Route("api/v1/admin/transactions")]
public class AdminTransactionsController : ApiControllerBase
{
    /// <summary>
    /// Tra cứu danh sách Escrow, số tiền cọc đang giữ, thời gian unlock, giải ngân, hoàn tiền kèm bộ lọc trạng thái.
    /// </summary>
    /// <param name="query">Các tham số phân trang, bộ lọc trạng thái Escrow/Payout, khoảng ngày và từ khóa tìm kiếm.</param>
    /// <param name="ct">CancellationToken.</param>
    /// <returns>Danh sách giao dịch phân trang kèm số liệu thống kê tổng hợp cho Admin.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AdminTransactionsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTransactions([FromQuery] GetAdminTransactionsQuery query, CancellationToken ct)
    {
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }
}

