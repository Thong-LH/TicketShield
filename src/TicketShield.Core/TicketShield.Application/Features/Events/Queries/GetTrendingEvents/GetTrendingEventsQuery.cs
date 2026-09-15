using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Events.Queries.GetTrendingEvents;

public class GetTrendingEventsQuery : IRequest<ApiResponse<List<TrendingEventDto>>>
{
    public int Limit { get; set; } = 6;
    public string? Category { get; set; }
    public string? City { get; set; }

    public GetTrendingEventsQuery() { }

    public GetTrendingEventsQuery(int limit = 6, string? category = null, string? city = null)
    {
        Limit = limit > 0 ? limit : 6;
        Category = category;
        City = city;
    }
}
