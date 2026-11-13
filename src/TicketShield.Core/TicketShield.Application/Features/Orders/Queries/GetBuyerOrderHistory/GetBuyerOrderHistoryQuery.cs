using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Orders.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Orders.Queries.GetBuyerOrderHistory;

/// <summary>
/// MediatR Query lấy danh sách các giao dịch mua vé của User (Mã đơn, tên sự kiện, số tiền đã chuyển khoản qua VietQR, mã tham chiếu SePay, trạng thái hoàn tiền).
/// Đáp ứng yêu cầu SCRUM-340 / SCRUM-341 (BE-11.2.1)
/// </summary>
public class GetBuyerOrderHistoryQuery : IRequest<ApiResponse<BuyerOrderHistoryResponseDto>>
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public EscrowStatus? Status { get; set; }
    public string? Keyword { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    public GetBuyerOrderHistoryQuery() { }

    public GetBuyerOrderHistoryQuery(
        int page = 1,
        int size = 20,
        EscrowStatus? status = null,
        string? keyword = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        Page = page > 0 ? page : 1;
        Size = size > 0 ? size : 20;
        Status = status;
        Keyword = keyword;
        FromDate = fromDate;
        ToDate = toDate;
    }
}

