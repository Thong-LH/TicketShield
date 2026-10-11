using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Admin.EventMarkup.Models;

namespace TicketShield.Application.Features.Admin.EventMarkup.Queries.GetAllEventResaleMarkups;

public class GetAllEventResaleMarkupsQueryHandler : IRequestHandler<GetAllEventResaleMarkupsQuery, ApiResponse<List<EventResaleMarkupDto>>>
{
    private readonly ITicketShieldDbContext _db;

    public GetAllEventResaleMarkupsQueryHandler(ITicketShieldDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<List<EventResaleMarkupDto>>> Handle(GetAllEventResaleMarkupsQuery request, CancellationToken cancellationToken)
    {
        var events = await _db.Events.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var result = events.Select(ev => new EventResaleMarkupDto
        {
            EventId = ev.Id,
            EventName = ev.Name,
            MaxResaleMarkupPercentage = ev.MaxResaleMarkupPercentage,
            Venue = ev.Venue,
            Category = ev.Category ?? "CONCERT",
            EventStartAt = ev.EventStartAt
        }).ToList();

        return ApiResponse<List<EventResaleMarkupDto>>.SuccessResponse(result, "Lấy danh sách trần giá sự kiện thành công.");
    }
}
