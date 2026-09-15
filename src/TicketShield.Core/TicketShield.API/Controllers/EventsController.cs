using Microsoft.AspNetCore.Mvc;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Events.Queries.GetTrendingEvents;

namespace TicketShield.API.Controllers;

[Route("api/v1/events")]
public class EventsController : ApiControllerBase
{
    /// <summary>
    /// Lấy danh sách sự kiện nổi bật (Trending / HOT) phục vụ Banner Carousel trang chủ
    /// </summary>
    [HttpGet("trending")]
    [ProducesResponseType(typeof(ApiResponse<List<TrendingEventDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTrending(
        [FromQuery] int limit = 6,
        [FromQuery] string? category = null,
        [FromQuery] string? city = null,
        CancellationToken ct = default)
    {
        var query = new GetTrendingEventsQuery(limit, category, city);
        var result = await Mediator.Send(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách các danh mục thể loại sự kiện
    /// </summary>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(ApiResponse<List<string>>), StatusCodes.Status200OK)]
    public IActionResult GetCategories()
    {
        var categories = new List<string>
        {
            "ALL",
            "CONCERT",
            "FESTIVAL",
            "SPORTS",
            "THEATER",
            "EXHIBITION"
        };

        return Ok(ApiResponse<List<string>>.SuccessResponse(categories, "Lấy danh mục sự kiện thành công."));
    }
}
