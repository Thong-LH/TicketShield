using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Orders.Models;

/// <summary>
/// DTO phản hồi danh sách lịch sử đơn hàng phân trang kèm tóm tắt tài chính cho Buyer
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
public class BuyerOrderHistoryResponseDto
{
    public PaginatedList<BuyerOrderDto> Orders { get; set; } = new();
    public BuyerOrderSummaryDto Summary { get; set; } = new();
}

