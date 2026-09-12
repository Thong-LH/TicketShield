using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Common;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMarketplaceListings;

public class GetMarketplaceListingsQueryHandler : IRequestHandler<GetMarketplaceListingsQuery, ApiResponse<List<ResaleListingDetailDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetMarketplaceListingsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<List<ResaleListingDetailDto>>> Handle(GetMarketplaceListingsQuery request, CancellationToken cancellationToken)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 20;

        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .Where(l =>
                !l.IsPrivate &&
                (l.ListingStatus == ListingStatus.Verified || l.ListingStatus == ListingStatus.Transacting));

        if (request.EventId.HasValue && request.EventId.Value != Guid.Empty)
        {
            query = query.Where(l => l.EventId == request.EventId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(l =>
                (l.Event != null && l.Event.Name.ToLower().Contains(kw)) ||
                (l.Event != null && l.Event.Venue.ToLower().Contains(kw)));
        }

        var listings = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var dtos = listings.Select(l => l.ToDetailDto()).ToList();

        return ApiResponse<List<ResaleListingDetailDto>>.SuccessResponse(dtos, "Lấy danh sách vé thị trường thành công.");
    }
}
