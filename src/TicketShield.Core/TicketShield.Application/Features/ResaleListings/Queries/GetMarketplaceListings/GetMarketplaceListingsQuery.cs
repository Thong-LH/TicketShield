using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMarketplaceListings;

public class GetMarketplaceListingsQuery : IRequest<ApiResponse<List<ResaleListingDetailDto>>>
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 20;
    public string? Keyword { get; set; }
    public Guid? EventId { get; set; }

    public GetMarketplaceListingsQuery() { }

    public GetMarketplaceListingsQuery(int page, int size, string? keyword = null, Guid? eventId = null)
    {
        Page = page > 0 ? page : 1;
        Size = size > 0 ? size : 20;
        Keyword = keyword;
        EventId = eventId;
    }
}
