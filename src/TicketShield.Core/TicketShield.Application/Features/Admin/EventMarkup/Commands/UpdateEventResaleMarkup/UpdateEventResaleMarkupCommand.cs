using MediatR;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;

namespace TicketShield.Application.Features.Admin.EventMarkup.Commands.UpdateEventResaleMarkup;

public class UpdateEventResaleMarkupCommand : IRequest<ApiResponse<EventResaleMarkupDto>>
{
    public Guid EventId { get; set; }
    public decimal MaxResaleMarkupPercentage { get; set; }
}
