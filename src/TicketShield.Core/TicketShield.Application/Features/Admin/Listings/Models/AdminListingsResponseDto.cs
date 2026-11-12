using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Admin.Listings.Models;

/// <summary>
/// DTO phản hồi danh sách tin đăng phân trang kèm số liệu tổng hợp cho Admin
/// </summary>
public class AdminListingsResponseDto
{
    public PaginatedList<AdminListingDto> Listings { get; set; } = new();
    public AdminListingsSummaryDto Summary { get; set; } = new();
}

