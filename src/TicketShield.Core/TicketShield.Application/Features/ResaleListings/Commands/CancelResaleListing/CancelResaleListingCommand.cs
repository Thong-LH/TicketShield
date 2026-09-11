using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Commands.CancelResaleListing;

public class CancelResaleListingCommand : IRequest<ApiResponse<CancelResaleListingResponse>>
{
    public Guid ListingId { get; set; }

    public CancelResaleListingCommand()
    {
    }

    public CancelResaleListingCommand(Guid listingId)
    {
        ListingId = listingId;
    }
}
