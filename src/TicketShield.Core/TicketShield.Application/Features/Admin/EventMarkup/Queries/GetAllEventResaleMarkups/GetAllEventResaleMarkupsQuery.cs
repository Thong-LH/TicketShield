using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;

namespace TicketShield.Application.Features.Admin.EventMarkup.Queries.GetAllEventResaleMarkups;

public class GetAllEventResaleMarkupsQuery : IRequest<ApiResponse<List<EventResaleMarkupDto>>>
{
}
