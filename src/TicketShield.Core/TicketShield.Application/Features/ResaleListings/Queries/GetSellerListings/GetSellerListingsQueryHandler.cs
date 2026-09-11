using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;

public class GetSellerListingsQueryHandler : IRequestHandler<GetSellerListingsQuery, ApiResponse<List<SellerListingDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly ICurrentUserService? _currentUserService;

    public GetSellerListingsQueryHandler(ITicketShieldDbContext dbContext, ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<ApiResponse<List<SellerListingDto>>> Handle(GetSellerListingsQuery request, CancellationToken cancellationToken)
    {
        // 1. Resolve SellerId from current authenticated user or fallback to first user in dev mode
        var sellerId = _currentUserService?.UserId ?? Guid.Empty;
        if (sellerId == Guid.Empty)
        {
            var defaultSeller = await _dbContext.Users
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (defaultSeller == null)
            {
                throw new NotFoundException("Không tìm thấy thông tin người bán trong hệ thống.");
            }
            sellerId = defaultSeller.Id;
        }

        // 2. Query seller's resale listings with Event and Tier navigation
        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Where(l => l.SellerId == sellerId);

        if (request.Status.HasValue)
        {
            query = query.Where(l => l.ListingStatus == request.Status.Value);
        }

        var listings = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        // 3. Map entities to DTOs
        var dtos = listings.Select(l =>
        {
            var discount = l.OriginalPrice - l.ResalePrice;
            var discountPercent = l.OriginalPrice > 0 ? Math.Round((discount / l.OriginalPrice) * 100, 1) : 0;

            return new SellerListingDto
            {
                ListingId = l.Id,
                EventId = l.EventId,
                EventName = l.Event?.Name ?? string.Empty,
                EventVenue = l.Event?.Venue ?? string.Empty,
                EventStartAt = l.Event?.EventStartAt ?? default,
                TierId = l.TierId,
                TierName = l.Tier?.TierName ?? string.Empty,
                OriginalTicketCode = l.OriginalTicketCode,
                OriginalPrice = l.OriginalPrice,
                ResalePrice = l.ResalePrice,
                DiscountAmount = discount > 0 ? discount : 0,
                DiscountPercentage = discountPercent > 0 ? discountPercent : 0,
                IsPrivate = l.IsPrivate,
                PrivateAccessToken = l.PrivateAccessToken,
                ShareUrl = l.IsPrivate && !string.IsNullOrEmpty(l.PrivateAccessToken)
                    ? $"https://ticketshield.vn/p/{l.PrivateAccessToken}"
                    : null,
                VerificationStatus = l.VerificationStatus.ToString(),
                ListingStatus = l.ListingStatus.ToString(),
                CreatedAt = l.CreatedAt
            };
        }).ToList();

        return ApiResponse<List<SellerListingDto>>.SuccessResponse(dtos, "Lấy danh sách vé niêm yết của người bán thành công.");
    }
}
