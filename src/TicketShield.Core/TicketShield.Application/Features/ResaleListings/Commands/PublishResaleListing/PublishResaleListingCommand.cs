using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.Application.Features.ResaleListings.Commands.PublishResaleListing;

/// <summary>
/// Command niêm yết vé lên thị trường sau khi đã xác thực và khóa vé thành công qua MediatR (US-2.3).
/// </summary>
public sealed record PublishResaleListingCommand(
    string Seller,
    string IdempotencyKey,
    PublishBody Body
) : IRequest<ApiResponse<VerificationResult>>;
