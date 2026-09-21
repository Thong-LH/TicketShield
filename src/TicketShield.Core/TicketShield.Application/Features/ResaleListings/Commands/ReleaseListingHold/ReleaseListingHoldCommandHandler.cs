using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;

public class ReleaseListingHoldCommandHandler : IRequestHandler<ReleaseListingHoldCommand, ApiResponse<ReleaseListingHoldResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public ReleaseListingHoldCommandHandler(
        ITicketShieldDbContext dbContext,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<ReleaseListingHoldResponse>> Handle(ReleaseListingHoldCommand request, CancellationToken cancellationToken)
    {
        // 1. Authenticate Buyer
        var buyerId = _currentUserService?.UserId ?? Guid.Empty;
        if (buyerId == Guid.Empty)
        {
            throw new UnauthorizedException("Bạn phải đăng nhập để hủy giữ chỗ vé.");
        }

        // 2. Fetch Resale Listing (không load EscrowTransactions — sẽ query riêng với SQL filter)
        var listing = await _dbContext.ResaleListings
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        // 3. Business Rule Validation: Must be in Transacting status
        if (listing.ListingStatus != ListingStatus.Transacting)
        {
            throw new BusinessRuleViolationException($"Bài đăng bán vé hiện không ở trạng thái giữ chỗ (TRANSACTING). Trạng thái hiện tại: '{listing.ListingStatus}'.");
        }

        // SQL-level filter: chỉ lấy escrow Pending của listing này
        var activeEscrow = await _dbContext.EscrowTransactions
            .Where(e => e.ListingId == request.ListingId && e.Status == EscrowStatus.Pending)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // 4. Validate Buyer Ownership: Only the holding buyer can cancel their hold
        if (activeEscrow != null && activeEscrow.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException("Bạn không phải người đang giữ chỗ bài đăng bán vé này.");
        }

        // 5. Update Status & Release Hold Immediately
        listing.ListingStatus = ListingStatus.Verified;
        if (activeEscrow != null)
        {
            activeEscrow.UnlockAt = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new ReleaseListingHoldResponse
        {
            ListingId = listing.Id,
            ListingStatus = listing.ListingStatus.ToString(),
            ReleasedAt = DateTimeOffset.UtcNow
        };

        return ApiResponse<ReleaseListingHoldResponse>.SuccessResponse(
            response,
            "Hủy giữ chỗ vé thành công, vé đã được trả về chợ bán.");
    }
}
