using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetSellerListings;

public class GetSellerListingsQuery : IRequest<ApiResponse<List<SellerListingDto>>>
{
    public ListingStatus? Status { get; set; }

    public GetSellerListingsQuery(ListingStatus? status = null)
    {
        Status = status;
    }
}
