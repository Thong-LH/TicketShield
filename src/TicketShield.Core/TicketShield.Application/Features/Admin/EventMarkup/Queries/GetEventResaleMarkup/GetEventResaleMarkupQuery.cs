using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;

namespace TicketShield.Application.Features.Admin.EventMarkup.Queries.GetEventResaleMarkup;

public record GetEventResaleMarkupQuery(Guid EventId) : IRequest<ApiResponse<EventResaleMarkupDto>>;
