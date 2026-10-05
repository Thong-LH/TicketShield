using MediatR;
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

        var detailDto = listing.ToDetailDto();

        if (listing.BundleId.HasValue)
        {
            var bundleListings = await _dbContext.ResaleListings
                .AsNoTracking()
                .Include(l => l.Event)
                .Include(l => l.Tier)
                .Include(l => l.Seller)
                .Where(l => l.BundleId == listing.BundleId.Value)
                .OrderBy(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .ToListAsync(cancellationToken);

            if (bundleListings.Count > 1)
            {
                detailDto.BundleItems = bundleListings.Select(b => b.ToDetailDto()).ToList();
            }
        }

        return ApiResponse<ResaleListingDetailDto>.SuccessResponse(detailDto, "Lấy thông tin vé bán riêng tư thành công.");
    }
}
