using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.Application.Features.ResaleListings.Commands.CreateMultiResaleListing;

/// <summary>
/// Command đăng bán danh sách N vé thành bundle qua MediatR (SCRUM-167 / BE-CORE-5.2.2).
/// Hỗ trợ 1 Root Idempotency-Key duy nhất cho toàn bộ batch và bọc trong 1 Transaction ACID duy nhất.
/// </summary>
public sealed record CreateMultiResaleListingCommand : IRequest<ApiResponse<BulkPublishResult>>
{
    public string Seller { get; init; } = string.Empty;
    public string RootIdempotencyKey { get; init; } = string.Empty;
    public BulkPublishBody Body { get; init; } = null!;
}
