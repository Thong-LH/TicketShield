using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.ResaleListings.Queries.GetMyPurchasedTickets;

public class GetMyPurchasedTicketsQuery : IRequest<ApiResponse<List<PurchasedTicketDto>>>
{
}
