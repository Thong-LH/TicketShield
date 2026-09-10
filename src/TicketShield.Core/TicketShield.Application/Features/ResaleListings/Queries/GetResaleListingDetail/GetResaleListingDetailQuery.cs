using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

public class GetResaleListingDetailQuery : IRequest<ApiResponse<ResaleListingDetailDto>>
{
    public Guid ListingId { get; set; }
    public string? Token { get; set; }

    public GetResaleListingDetailQuery(Guid listingId, string? token = null)
    {
        ListingId = listingId;
        Token = token;
    }
}
