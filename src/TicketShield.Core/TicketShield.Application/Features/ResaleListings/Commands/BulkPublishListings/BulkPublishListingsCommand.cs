using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.Application.Features.ResaleListings.Commands.BulkPublishListings;

/// <summary>
/// Command đăng bán danh sách vé thành bundle (SCRUM-167 / BE-CORE-5.2.2)
/// </summary>
public sealed record BulkPublishListingsCommand : IRequest<ApiResponse<BulkPublishResult>>
{
    public string Seller { get; init; } = string.Empty;
    public BulkPublishBody Body { get; init; } = null!;
}
