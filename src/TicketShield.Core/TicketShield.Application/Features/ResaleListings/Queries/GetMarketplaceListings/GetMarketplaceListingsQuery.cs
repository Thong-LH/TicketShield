using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMarketplaceListings;

public class GetMarketplaceListingsQuery : IRequest<ApiResponse<PaginatedList<ResaleListingDetailDto>>>
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? Keyword { get; set; }
    public Guid? EventId { get; set; }
    public Guid? OrganizerId { get; set; }
    /// <summary>Event category: CONCERT, FESTIVAL, SPORTS, THEATER (ALL or empty = no filter).</summary>
    public string? Category { get; set; }
    /// <summary>City code used by the web filter: HN, HCM, DN (ALL or empty = no filter).</summary>
    public string? City { get; set; }
    /// <summary>upcoming (next 7 days) or this-month; anything else = no filter.</summary>
    public string? DateRange { get; set; }
    /// <summary>newest (default), price-asc or date-asc.</summary>
    public string? SortBy { get; set; }

    public GetMarketplaceListingsQuery() { }

    public GetMarketplaceListingsQuery(int page, int size, string? keyword = null, Guid? eventId = null, Guid? organizerId = null)
    {
        Page = page > 0 ? page : 1;
        Size = size > 0 ? size : 20;
        Keyword = keyword;
        EventId = eventId;
        OrganizerId = organizerId;
    }
}
