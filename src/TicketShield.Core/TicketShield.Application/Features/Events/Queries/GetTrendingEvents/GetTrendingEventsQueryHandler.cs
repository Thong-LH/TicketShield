using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.Events.Queries.GetTrendingEvents;

public class GetTrendingEventsQueryHandler : IRequestHandler<GetTrendingEventsQuery, ApiResponse<List<TrendingEventDto>>>
{
    private readonly ITicketShieldDbContext _dbContext;

    public GetTrendingEventsQueryHandler(ITicketShieldDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<List<TrendingEventDto>>> Handle(GetTrendingEventsQuery request, CancellationToken cancellationToken)
    {
        var limit = request.Limit > 0 ? request.Limit : 6;
        var now = DateTimeOffset.UtcNow;

        var query = _dbContext.Events
            .AsNoTracking()
            .Include(e => e.Organizer)
            .Include(e => e.TicketTiers)
            .Include(e => e.ResaleListings)
            .Where(e => e.Status == "UPCOMING" && e.EventStartAt > now);

        if (!string.IsNullOrWhiteSpace(request.Category) && !request.Category.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var cat = request.Category.Trim().ToUpperInvariant();
            query = query.Where(e => e.Category != null && e.Category.ToUpper() == cat);
        }

        if (!string.IsNullOrWhiteSpace(request.City) && !request.City.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var city = request.City.Trim().ToLower();
            query = query.Where(e => e.City != null && e.City.ToLower().Contains(city));
        }

        var events = await query
            .OrderByDescending(e => e.ResaleListings.Count(l => !l.IsPrivate && (l.ListingStatus == ListingStatus.Verified || l.ListingStatus == ListingStatus.Transacting)))
            .ThenBy(e => e.EventStartAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var dtos = events.Select(e =>
        {
            var availableListings = e.ResaleListings
                .Where(l => !l.IsPrivate && l.ListingStatus == ListingStatus.Verified)
                .ToList();

            var minResale = availableListings.Any()
                ? availableListings.Min(l => l.ResalePrice)
                : (decimal?)null;

            var originalPriceFrom = e.TicketTiers.Any()
                ? e.TicketTiers.Min(t => t.OriginalPrice)
                : (decimal?)null;

            return new TrendingEventDto
            {
                Id = e.Id,
                Name = e.Name,
                Artist = e.Artist,
                Category = e.Category ?? "CONCERT",
                City = e.City ?? "TP. Hồ Chí Minh",
                BannerUrl = e.BannerUrl,
                Venue = e.Venue,
                Description = e.Description,
                EventStartAt = e.EventStartAt,
                EventEndAt = e.EventEndAt,
                MinResalePrice = minResale,
                OriginalPriceFrom = originalPriceFrom,
                TotalAvailableListings = availableListings.Count,
                OrganizerName = e.Organizer?.Name ?? string.Empty
            };
        }).ToList();

        return ApiResponse<List<TrendingEventDto>>.SuccessResponse(dtos, "Lấy danh sách sự kiện nổi bật thành công.");
    }
}
