using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.ReleaseListingHold;

public class ReleaseListingHoldCommand : IRequest<ApiResponse<ReleaseListingHoldResponse>>
{
    public Guid ListingId { get; set; }

    public ReleaseListingHoldCommand() { }

    public ReleaseListingHoldCommand(Guid listingId)
    {
        ListingId = listingId;
    }
}
