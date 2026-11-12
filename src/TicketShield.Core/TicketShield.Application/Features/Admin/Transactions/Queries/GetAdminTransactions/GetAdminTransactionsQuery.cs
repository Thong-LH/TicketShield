using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.Transactions.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Admin.Transactions.Queries.GetAdminTransactions;

/// <summary>
/// MediatR Query tra cứu danh sách Escrow, số tiền cọc đang giữ, thời gian unlock, giải ngân, hoàn tiền kèm bộ lọc trạng thái.
/// Đáp ứng yêu cầu SCRUM-333 / SCRUM-334 (BE-11.1.1)
/// </summary>
public class GetAdminTransactionsQuery : IRequest<ApiResponse<AdminTransactionsResponseDto>>
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public EscrowStatus? Status { get; set; }
    public PayoutStatus? PayoutStatus { get; set; }
    public bool? InSettlementBufferOnly { get; set; }
    public string? Keyword { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }

    public GetAdminTransactionsQuery() { }

    public GetAdminTransactionsQuery(
        int page = 1,
        int size = 20,
        EscrowStatus? status = null,
        PayoutStatus? payoutStatus = null,
        bool? inSettlementBufferOnly = null,
        string? keyword = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null)
    {
        Page = page > 0 ? page : 1;
        Size = size > 0 ? size : 20;
        Status = status;
        PayoutStatus = payoutStatus;
        InSettlementBufferOnly = inSettlementBufferOnly;
        Keyword = keyword;
        FromDate = fromDate;
        ToDate = toDate;
    }
}

