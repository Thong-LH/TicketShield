using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetPaymentStatus;

public sealed class GetPaymentStatusQueryHandler : IRequestHandler<GetPaymentStatusQuery, ApiResponse<GetPaymentStatusDto>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetPaymentStatusQueryHandler(
        ITicketShieldDbContext dbContext,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<GetPaymentStatusDto>> Handle(GetPaymentStatusQuery request, CancellationToken cancellationToken)
    {
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("You must be signed in to view payment status.");
        }

        // FIX Lỗi 13: EscrowTransaction chỉ lưu ListingId của vé đại diện (vé đầu tiên trong Combo).
        // Khi Buyer check trạng thái cho vé con thứ 2/3/4, cần tìm theo BundleId.
        var dto = await _dbContext.EscrowTransactions
            .AsNoTracking()
            .Where(e => (e.ListingId == request.ListingId
                         || (_dbContext.ResaleListings
                                 .Where(l => l.Id == request.ListingId && l.BundleId != null)
                                 .Select(l => (Guid?)l.BundleId)
                                 .FirstOrDefault()) != null
                             && e.BundleId == _dbContext.ResaleListings
                                 .Where(l => l.Id == request.ListingId)
                                 .Select(l => (Guid?)l.BundleId)
                                 .FirstOrDefault())
                        && e.BuyerId == buyerId)
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new GetPaymentStatusDto
            {
                ListingId = e.ListingId,
                EscrowId = e.Id,
                ListingStatus = e.Listing.ListingStatus.ToString(),
                EscrowStatus = e.Status.ToString(),
                PaymentReference = e.PaymentReference,
                UnlockAt = e.UnlockAt,
                InSettlementBuffer = e.InSettlementBuffer
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (dto == null)
        {
            // Phân biệt 403 vs 404: kiểm tra listing có escrow của người khác không
            var hasOtherEscrow = await _dbContext.EscrowTransactions
                .AsNoTracking()
                .AnyAsync(e => e.ListingId == request.ListingId, cancellationToken);

            if (hasOtherEscrow)
            {
                throw new ForbiddenAccessException("You do not have permission to view the payment status of this transaction.");
            }

            var listingExists = await _dbContext.ResaleListings
                .AsNoTracking()
                .AnyAsync(l => l.Id == request.ListingId, cancellationToken);

            if (!listingExists)
            {
                throw new NotFoundException("Ticket listing", request.ListingId);
            }

            throw new NotFoundException("Escrow transaction", request.ListingId);
        }

        return ApiResponse<GetPaymentStatusDto>.SuccessResponse(dto, "Payment status retrieved successfully.");
    }
}
