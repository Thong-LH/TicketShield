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
        var now = DateTimeOffset.UtcNow;

        var query = _dbContext.ResaleListings
            .AsNoTracking()
            .Include(l => l.Event)
                .ThenInclude(e => e.Organizer)
            .Include(l => l.Tier)
            .Include(l => l.Seller)
            .Include(l => l.EscrowTransactions)
            .Where(l =>
                !l.IsPrivate &&
                (l.ListingStatus == ListingStatus.Verified || l.ListingStatus == ListingStatus.Transacting) &&
                // FIX Lỗi 24 / BR-L04: Sàn đóng cửa trước giờ khai mạc 2 tiếng.
                // Tránh UX xấu: buyer thấy vé nhưng bấm mua nhận 400 BusinessRuleViolationException.
                l.Event != null && l.Event.EventStartAt.AddHours(-2) > now);

        if (request.EventId.HasValue && request.EventId.Value != Guid.Empty)
        {
            query = query.Where(l => l.EventId == request.EventId.Value);
        }

        if (request.OrganizerId.HasValue && request.OrganizerId.Value != Guid.Empty)
        {
            query = query.Where(l => l.Event != null && l.Event.OrganizerId == request.OrganizerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var kw = request.Keyword.Trim().ToLower();
            query = query.Where(l =>
                (l.Event != null && l.Event.Name.ToLower().Contains(kw)) ||
                (l.Event != null && l.Event.Venue.ToLower().Contains(kw)));
        }

        // TC-MKT-01 / TC-MKT-03: every filter and the sort run here, on all listings,
        // so the total and each page are correct (the web used to filter one page only).
        var category = request.Category?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(category) && category != "ALL")
        {
            query = query.Where(l => l.Event != null && l.Event.Category != null && l.Event.Category.ToUpper() == category);
        }

        query = (request.City?.Trim().ToUpperInvariant()) switch
        {
            "HN" => query.Where(l => l.Event != null && (
                (l.Event.City != null && (l.Event.City.ToLower().Contains("hà nội") || l.Event.City.ToLower().Contains("ha noi") || l.Event.City.ToLower().Contains("hanoi"))) ||
                l.Event.Venue.ToLower().Contains("hà nội") || l.Event.Venue.ToLower().Contains("hanoi"))),
            "HCM" => query.Where(l => l.Event != null && (
                (l.Event.City != null && (l.Event.City.ToLower().Contains("hồ chí minh") || l.Event.City.ToLower().Contains("ho chi minh") || l.Event.City.ToLower().Contains("hcm"))) ||
                l.Event.Venue.ToLower().Contains("hồ chí minh") || l.Event.Venue.ToLower().Contains("tp.hcm"))),
            "DN" => query.Where(l => l.Event != null && (
                (l.Event.City != null && (l.Event.City.ToLower().Contains("đà nẵng") || l.Event.City.ToLower().Contains("da nang"))) ||
                l.Event.Venue.ToLower().Contains("đà nẵng") || l.Event.Venue.ToLower().Contains("da nang"))),
            _ => query
        };

        var vietnamNow = now.ToOffset(TimeSpan.FromHours(7));
        switch (request.DateRange?.Trim().ToLowerInvariant())
        {
            case "upcoming":
                var weekEnd = now.AddDays(7);
                query = query.Where(l => l.Event != null && l.Event.EventStartAt <= weekEnd);
                break;
            case "this-month":
                var monthStart = new DateTimeOffset(vietnamNow.Year, vietnamNow.Month, 1, 0, 0, 0, vietnamNow.Offset);
                var monthEnd = monthStart.AddMonths(1);
                query = query.Where(l => l.Event != null && l.Event.EventStartAt >= monthStart && l.Event.EventStartAt < monthEnd);
                break;
        }

        var totalCount = await query.CountAsync(cancellationToken);

        IOrderedQueryable<TicketShield.Domain.Entities.ResaleListing> ordered = (request.SortBy?.Trim().ToLowerInvariant()) switch
        {
            "price-asc" => query.OrderBy(l => l.ResalePrice).ThenByDescending(l => l.CreatedAt),
            "date-asc" => query.OrderBy(l => l.Event!.EventStartAt).ThenByDescending(l => l.CreatedAt),
            _ => query.OrderByDescending(l => l.CreatedAt)
        };

        var listings = await ordered
            .ThenBy(l => l.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // A combo creates one escrow row against its anchor listing. Supply
        // that exact hold expiry to every listing in the bundle so all clients
        // see the same server-side countdown.
        var bundleIds = listings
            .Where(l => l.BundleId.HasValue)
            .Select(l => l.BundleId!.Value)
            .Distinct()
            .ToList();
        var bundleUnlocks = bundleIds.Count == 0
            ? new Dictionary<Guid, DateTimeOffset>()
            : await _dbContext.EscrowTransactions
                .AsNoTracking()
                .Where(e => e.BundleId.HasValue && bundleIds.Contains(e.BundleId.Value)
                    && e.Status == EscrowStatus.Pending && e.UnlockAt.HasValue && e.UnlockAt.Value > now)
                .GroupBy(e => e.BundleId!.Value)
                .Select(group => new { BundleId = group.Key, UnlockAt = group.Max(e => e.UnlockAt!.Value) })
                .ToDictionaryAsync(item => item.BundleId, item => item.UnlockAt, cancellationToken);

        // NOTE: Không áp dụng .Select() DTO projection ở SQL vì ResaleListing có computed properties
        // (DiscountAmount, DiscountPercentage, MaskedTicketCode) không thể translate sang SQL.
        // Entity được load đầy đủ, DTO mapping thực hiện in-memory qua ToDetailDto().
        var dtos = listings
            .Select(l => l.ToDetailDto(l.BundleId.HasValue && bundleUnlocks.TryGetValue(l.BundleId.Value, out var unlockAt) ? unlockAt : null))
            .ToList();
        var paginatedList = new PaginatedList<ResaleListingDetailDto>(dtos, totalCount, page, size);

        return ApiResponse<PaginatedList<ResaleListingDetailDto>>.SuccessResponse(paginatedList, "Marketplace ticket listings retrieved successfully.");
    }
}
