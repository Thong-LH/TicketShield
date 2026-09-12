using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Common;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMarketplaceListings;

public class GetMarketplaceListingsQueryHandler : IRequestHandler<GetMarketplaceListingsQuery, ApiResponse<PaginatedList<ResaleListingDetailDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetMarketplaceListingsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<PaginatedList<ResaleListingDetailDto>>> Handle(GetMarketplaceListingsQuery request, CancellationToken cancellationToken)
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

        var totalCount = await query.CountAsync(cancellationToken);

        var listings = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        var dtos = listings.Select(l => l.ToDetailDto()).ToList();
        var paginatedList = new PaginatedList<ResaleListingDetailDto>(dtos, totalCount, page, size);

        return ApiResponse<PaginatedList<ResaleListingDetailDto>>.SuccessResponse(paginatedList, "Lấy danh sách vé thị trường thành công.");
    }
}
