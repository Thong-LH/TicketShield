using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingDetail;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetResaleListingByPrivateToken;

public class GetResaleListingByPrivateTokenQuery : IRequest<ApiResponse<ResaleListingDetailDto>>
{
    public string ShareToken { get; set; }

    public GetResaleListingByPrivateTokenQuery(string shareToken)
    {
        ShareToken = shareToken;
    }
}
