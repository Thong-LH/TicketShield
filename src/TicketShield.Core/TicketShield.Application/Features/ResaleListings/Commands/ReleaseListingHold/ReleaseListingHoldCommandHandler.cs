using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
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

        // 2. Fetch Resale Listing with Event for cutoff verification
        var listing = await _dbContext.ResaleListings
            .Include(l => l.Event)
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

        // BE-CORE-5.2.3: Bundle-aware release
        if (listing.BundleId != null && listing.IsBundleAllOrNothing)
        {
            return await ReleaseBundleHoldAsync(listing, buyerId, cancellationToken);
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
        var now = DateTimeOffset.UtcNow;
        var pastResaleCutoff = listing.Event != null &&
            listing.Event.EventStartAt.AddHours(-2) <= now;
        listing.ListingStatus = pastResaleCutoff
            ? ListingStatus.Expired
            : ListingStatus.Verified;

        if (activeEscrow != null)
        {
            activeEscrow.Status = EscrowStatus.Cancelled;
            activeEscrow.UnlockAt = now;
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

    /// <summary>
    /// BE-CORE-5.2.3: Release all listings in an AllOrNothing bundle atomically.
    /// </summary>
    private async Task<ApiResponse<ReleaseListingHoldResponse>> ReleaseBundleHoldAsync(
        ResaleListing anchorListing,
        Guid buyerId,
        CancellationToken cancellationToken)
    {
        var bundleId = anchorListing.BundleId!.Value;

        // Find the bundle escrow
        var bundleEscrow = await _dbContext.EscrowTransactions
            .Where(e => e.BundleId == bundleId && e.Status == EscrowStatus.Pending)
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        // Validate buyer ownership
        if (bundleEscrow != null && bundleEscrow.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException("Bạn không phải người đang giữ chỗ gói vé này.");
        }

        // Query all listings in the bundle
        var bundleListings = await _dbContext.ResaleListings
            .Include(l => l.Event)
            .Where(l => l.BundleId == bundleId)
            .ToListAsync(cancellationToken);

        // Release all listings
        var now = DateTimeOffset.UtcNow;
        foreach (var listing in bundleListings)
        {
            if (listing.ListingStatus == ListingStatus.Transacting)
            {
                var pastResaleCutoff = listing.Event != null &&
                    listing.Event.EventStartAt.AddHours(-2) <= now;
                listing.ListingStatus = pastResaleCutoff
                    ? ListingStatus.Expired
                    : ListingStatus.Verified;
            }
        }

        // Cancel the bundle escrow
        if (bundleEscrow != null)
        {
            bundleEscrow.Status = EscrowStatus.Cancelled;
            bundleEscrow.UnlockAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new ReleaseListingHoldResponse
        {
            ListingId = anchorListing.Id,
            ListingStatus = anchorListing.ListingStatus.ToString(),
            ReleasedAt = now
        };

        return ApiResponse<ReleaseListingHoldResponse>.SuccessResponse(
            response,
            $"Hủy giữ chỗ gói {bundleListings.Count} vé thành công, vé đã được trả về chợ bán.");
    }
}
