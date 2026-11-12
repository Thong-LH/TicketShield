using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Admin.Transactions.Models;

/// <summary>
/// DTO phản hồi danh sách giao dịch phân trang kèm số liệu tổng hợp cho Admin
/// </summary>
public class AdminTransactionsResponseDto
{
    public PaginatedList<AdminTransactionDto> Transactions { get; set; } = new();
    public AdminTransactionsSummaryDto Summary { get; set; } = new();
}

