using System.Security.Cryptography;
using System.Text;
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

        // Quick routing check (without heavy lock yet)
        var routingListing = await _dbContext.ResaleListings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (routingListing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        // BE-CORE-5.2.3: Bundle-aware release
        if (routingListing.BundleId != null && routingListing.IsBundleAllOrNothing)
        {
            return await ReleaseBundleHoldAsync(routingListing, buyerId, cancellationToken);
        }

        // FIX Lỗi 10: Concurrency Control với PostgreSQL Advisory Lock theo ListingId
        await using var tx = await _dbContext.BeginAdvisoryLockTransactionAsync(ComputeLockKey(request.ListingId), cancellationToken);

        try
        {
            // 2. Fetch Resale Listing FRESH inside the locked transaction
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

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

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
        catch
        {
            if (tx != null)
            {
                await tx.RollbackAsync(cancellationToken);
            }
            throw;
        }
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

        // FIX Lỗi 10: Concurrency Control với PostgreSQL Advisory Lock theo BundleId
        await using var bundleTx = await _dbContext.BeginAdvisoryLockTransactionAsync(ComputeBundleLockKey(bundleId), cancellationToken);

        try
        {
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

            if (bundleTx != null)
            {
                await bundleTx.CommitAsync(cancellationToken);
            }

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
        catch
        {
            if (bundleTx != null)
            {
                await bundleTx.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    private static long ComputeLockKey(Guid listingId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:hold:listing:" + listingId));
        return BitConverter.ToInt64(hash, 0);
    }

    private static long ComputeBundleLockKey(Guid bundleId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ts:hold:bundle:" + bundleId));
        return BitConverter.ToInt64(hash, 0);
    }
}
