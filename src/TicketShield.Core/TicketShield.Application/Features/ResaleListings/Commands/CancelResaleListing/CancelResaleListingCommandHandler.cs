using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;

public class CancelResaleListingCommandHandler : IRequestHandler<CancelResaleListingCommand, ApiResponse<CancelResaleListingResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;
    private readonly ITicketVerificationService? _verificationService;

    public CancelResaleListingCommandHandler(
        ITicketShieldDbContext dbContext,
        ICurrentUserService? currentUserService = null,
        ITicketVerificationService? verificationService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _verificationService = verificationService;
    }

    public async Task<ApiResponse<CancelResaleListingResponse>> Handle(CancelResaleListingCommand request, CancellationToken cancellationToken)
    {
        // 1. Resolve seller ID from current authenticated user — no anonymous fallback
        var sellerId = _currentUserService?.UserId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
            throw new UnauthorizedException("Bạn phải đăng nhập để hủy tin đăng bán vé.");

        // 2. Initial lookup to determine lock scope (single listing vs. bundle)
        var routingListing = await _dbContext.ResaleListings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

        if (routingListing == null)
        {
            throw new NotFoundException("Tin đăng bán vé", request.ListingId);
        }

        if (routingListing.SellerId != sellerId)
        {
            throw new ForbiddenAccessException("Bạn không có quyền hủy tin đăng bán vé này.");
        }

        // 3. Concurrency Control: Advisory Lock transaction by BundleId or ListingId
        var lockKey = routingListing.BundleId.HasValue
            ? ComputeBundleLockKey(routingListing.BundleId.Value)
            : ComputeLockKey(routingListing.Id);

        await using var tx = await _dbContext.BeginAdvisoryLockTransactionAsync(lockKey, cancellationToken);

        try
        {
            // 4. Retrieve FRESH listing inside locked transaction
            var listing = await _dbContext.ResaleListings
                .FirstOrDefaultAsync(l => l.Id == request.ListingId, cancellationToken);

            if (listing == null)
            {
                throw new NotFoundException("Tin đăng bán vé", request.ListingId);
            }

            if (listing.SellerId != sellerId)
            {
                throw new ForbiddenAccessException("Bạn không có quyền hủy tin đăng bán vé này.");
            }

            if (listing.ListingStatus != ListingStatus.Verified)
            {
                throw new BusinessRuleViolationException(
                    $"Tin đăng bán vé hiện đang ở trạng thái '{listing.ListingStatus}'. Chỉ có thể hủy tin đăng khi vé chưa bị người mua đặt hoặc mua.");
            }

            var listingsToCancel = new List<ResaleListing> { listing };
            if (listing.BundleId.HasValue)
            {
                var bundleListings = await _dbContext.ResaleListings
                    .Where(l => l.BundleId == listing.BundleId.Value && l.SellerId == sellerId)
                    .ToListAsync(cancellationToken);

                if (bundleListings.Any(l => l.ListingStatus != ListingStatus.Verified && l.ListingStatus != ListingStatus.Cancelled))
                {
                    throw new BusinessRuleViolationException(
                        "Một hoặc nhiều vé trong gói vé (combo) này đang được giao dịch hoặc đã bán. Không thể hủy gói vé.");
                }

                listingsToCancel = bundleListings.Where(l => l.ListingStatus == ListingStatus.Verified).ToList();
            }

            // 5. Unlock original ticket(s) at MockOrganizer via verification service (gRPC)
            if (_verificationService != null)
            {
                var unlockedItems = new List<ResaleListing>();
                try
                {
                    foreach (var item in listingsToCancel)
                    {
                        var idempotencyKey = item.Id.ToString("D");
                        await _verificationService.CancelByListingId(sellerId.ToString("D"), item.Id, idempotencyKey, cancellationToken);
                        unlockedItems.Add(item);
                    }
                }
                catch (Exception)
                {
                    // If unlock fails for any ticket midway, throw a clear exception.
                    // The DB transaction tx will be rolled back in the catch block below.
                    throw new BusinessRuleViolationException(
                        $"Không thể mở khóa trọn gói vé với Ban Tổ Chức (đã mở khóa {unlockedItems.Count}/{listingsToCancel.Count} vé). Vui lòng thử lại sau.");
                }
            }

            // 6. Update listing status to CANCELLED in DB & SaveChanges
            foreach (var item in listingsToCancel)
            {
                item.ListingStatus = ListingStatus.Cancelled;
            }
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            var response = new CancelResaleListingResponse
            {
                ListingId = listing.Id,
                OriginalTicketCode = listing.OriginalTicketCode,
                AllCancelledTicketCodes = listingsToCancel
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id)
                    .Select(item => item.OriginalTicketCode)
                    .ToList(),
                ListingStatus = listing.ListingStatus.ToString(),
                CancelledAt = DateTimeOffset.UtcNow
            };

            return ApiResponse<CancelResaleListingResponse>.SuccessResponse(response, "Hủy tin đăng bán vé thành công và đã gửi yêu cầu mở khóa vé.");
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
