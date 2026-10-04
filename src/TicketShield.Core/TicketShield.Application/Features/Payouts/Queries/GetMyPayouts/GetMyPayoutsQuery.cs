using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Payouts.Queries.GetMyPayouts;

public class GetMyPayoutsQuery : IRequest<ApiResponse<List<MyPayoutDto>>>
{
}
