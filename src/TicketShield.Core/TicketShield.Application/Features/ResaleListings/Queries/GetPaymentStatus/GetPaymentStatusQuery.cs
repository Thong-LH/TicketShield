using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetPaymentStatus;

public sealed class GetPaymentStatusQuery : IRequest<ApiResponse<GetPaymentStatusDto>>
{
    public Guid ListingId { get; }

    public GetPaymentStatusQuery(Guid listingId)
    {
        ListingId = listingId;
    }
}
