using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingByPrivateToken;

public class GetResaleListingByPrivateTokenQueryHandler : IRequestHandler<GetResaleListingByPrivateTokenQuery, ApiResponse<ResaleListingDetailDto>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetResaleListingByPrivateTokenQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<ResaleListingDetailDto>> Handle(GetResaleListingByPrivateTokenQuery request, CancellationToken cancellationToken)
    {
        var cleanToken = request.ShareToken?.Trim();
        if (string.IsNullOrWhiteSpace(cleanToken))
        {
            throw new NotFoundException("Liên kết bán riêng tư không hợp lệ hoặc đã bị hủy.");
        }

        var listing = await _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.IsPrivate && l.PrivateAccessToken == cleanToken, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Liên kết bán riêng tư không hợp lệ hoặc đã bị hủy.");
        }

        // Mask ticket code to prevent admission barcode theft prior to escrow payment
        var rawCode = listing.OriginalTicketCode;
        var maskedCode = rawCode.Length <= 4
            ? "****"
            : string.Concat(rawCode.AsSpan(0, 2), new string('*', rawCode.Length - 4), rawCode.AsSpan(rawCode.Length - 2));

        var discount = listing.OriginalPrice - listing.ResalePrice;
        var discountPercent = listing.OriginalPrice > 0 ? Math.Round((discount / listing.OriginalPrice) * 100, 1) : 0;

        var dto = new ResaleListingDetailDto
        {
            ListingId = listing.Id,
            EventId = listing.EventId,
            EventName = listing.Event.Name,
            EventVenue = listing.Event.Venue,
            EventStartAt = listing.Event.EventStartAt,
            TierId = listing.TierId,
            TierName = listing.Tier.TierName,
            OriginalPrice = listing.OriginalPrice,
            ResalePrice = listing.ResalePrice,
            DiscountAmount = discount > 0 ? discount : 0,
            DiscountPercentage = discountPercent > 0 ? discountPercent : 0,
            IsPrivate = listing.IsPrivate,
            MaskedTicketCode = maskedCode,
            VerificationStatus = listing.VerificationStatus.ToString(),
            ListingStatus = listing.ListingStatus.ToString(),
            SellerId = listing.SellerId,
            SellerFullName = listing.Seller.FullName,
            CreatedAt = listing.CreatedAt
        };

        return ApiResponse<ResaleListingDetailDto>.SuccessResponse(dto, "Lấy thông tin vé bán riêng tư thành công.");
    }
}
