using MediatR;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.PublishResaleListing;

/// <summary>
/// Handler điều phối niêm yết vé qua MediatR, loại bỏ phụ thuộc trực tiếp tại Controller (Clean Architecture & CQRS).
/// </summary>
public class PublishResaleListingCommandHandler : IRequestHandler<PublishResaleListingCommand, ApiResponse<VerificationResult>>
{
    private readonly ITicketVerificationService? _verificationService;

    public PublishResaleListingCommandHandler(ITicketVerificationService? verificationService = null)
    {
        _verificationService = verificationService;
    }

    public async Task<ApiResponse<VerificationResult>> Handle(PublishResaleListingCommand request, CancellationToken cancellationToken)
    {
        if (_verificationService == null)
        {
            throw new ResaleWorkflowException("SERVICE_UNAVAILABLE", 503);
        }

        if (string.IsNullOrWhiteSpace(request.Seller))
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để thực hiện đăng bán vé.");
        }

        var result = await _verificationService.Publish(
            request.Seller,
            request.IdempotencyKey,
            request.Body,
            cancellationToken);

        return ApiResponse<VerificationResult>.SuccessResponse(result, result.Status);
    }
}
