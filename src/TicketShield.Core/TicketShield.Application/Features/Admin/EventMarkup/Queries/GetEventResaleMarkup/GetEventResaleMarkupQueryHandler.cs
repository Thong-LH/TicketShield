using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Admin.EventMarkup.Queries.GetEventResaleMarkup;

public class GetEventResaleMarkupQueryHandler : IRequestHandler<GetEventResaleMarkupQuery, ApiResponse<EventResaleMarkupDto>>
{
    private readonly ITicketShieldDbContext _db;

    public GetEventResaleMarkupQueryHandler(ITicketShieldDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<EventResaleMarkupDto>> Handle(GetEventResaleMarkupQuery request, CancellationToken cancellationToken)
    {
        var ev = await _db.Events.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EventId, cancellationToken)
            ?? throw new NotFoundException("Sự kiện", request.EventId);

        return ApiResponse<EventResaleMarkupDto>.SuccessResponse(new EventResaleMarkupDto
        {
            EventId = ev.Id,
            EventName = ev.Name,
            MaxResaleMarkupPercentage = ev.MaxResaleMarkupPercentage
        }, "Lấy biên độ trần giá sự kiện thành công.");
    }
}
