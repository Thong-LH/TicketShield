using MediatR;
using TicketShield.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Common;
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
            throw new NotFoundException("Invalid or cancelled private share link.");
        }

        var listing = await _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .FirstOrDefaultAsync(l => l.IsPrivate && l.PrivateAccessToken == cleanToken, cancellationToken);

        if (listing == null)
        {
            throw new NotFoundException("Invalid or cancelled private share link.");
        }

        var detailDto = listing.ToDetailDto();

        // An old link still explains that the ticket was withdrawn (agreed with FE),
        // but it no longer exposes the price, the seller or the ticket (TC-MKT-07).
        if (listing.ListingStatus == ListingStatus.Cancelled)
        {
            return ApiResponse<ResaleListingDetailDto>.SuccessResponse(
                RedactCancelled(detailDto), "This private listing was cancelled by the seller.");
        }

        if (listing.BundleId.HasValue)
        {
            var bundleListings = await _dbContext.ResaleListings
                .AsNoTracking()
                .Include(l => l.Event)
                .Include(l => l.Tier)
                .Include(l => l.Seller)
                .Where(l => l.BundleId == listing.BundleId.Value && l.ListingStatus != ListingStatus.Cancelled)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            if (bundleListings.Count > 1)
            {
                detailDto.BundleItems = bundleListings.Select(b => b.ToDetailDto()).ToList();
            }
        }

        return ApiResponse<ResaleListingDetailDto>.SuccessResponse(detailDto, "Private ticket details retrieved successfully.");
    }

    private static ResaleListingDetailDto RedactCancelled(ResaleListingDetailDto dto)
    {
        dto.OriginalPrice = 0m;
        dto.ResalePrice = 0m;
        dto.DiscountAmount = 0m;
        dto.DiscountPercentage = 0m;
        dto.SellerId = Guid.Empty;
        dto.SellerFullName = string.Empty;
        dto.MaskedTicketCode = string.Empty;
        dto.SeatZone = null;
        dto.PrivateAccessToken = null;
        dto.UnlockAt = null;
        dto.BundleItems = null;
        return dto;
    }
}
