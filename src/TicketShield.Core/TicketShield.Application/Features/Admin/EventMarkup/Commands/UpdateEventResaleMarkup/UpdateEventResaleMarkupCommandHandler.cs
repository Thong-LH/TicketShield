using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.EventMarkup.Commands.UpdateEventResaleMarkup;

public class UpdateEventResaleMarkupCommandHandler : IRequestHandler<UpdateEventResaleMarkupCommand, ApiResponse<EventResaleMarkupDto>>
{
    private readonly ITicketShieldDbContext _db;

    public UpdateEventResaleMarkupCommandHandler(ITicketShieldDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<EventResaleMarkupDto>> Handle(UpdateEventResaleMarkupCommand request, CancellationToken cancellationToken)
    {
        var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện", request.EventId);

        ev.MaxResaleMarkupPercentage = request.MaxResaleMarkupPercentage;
        await _db.SaveChangesAsync(cancellationToken);

        return ApiResponse<EventResaleMarkupDto>.SuccessResponse(new EventResaleMarkupDto
        {
            EventId = ev.Id,
            EventName = ev.Name,
            MaxResaleMarkupPercentage = ev.MaxResaleMarkupPercentage
        }, "Cập nhật biên độ trần giá sự kiện thành công.");
    }
}
