using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/events")]
public class EventTiersController(OrganizerDbContext db) : ControllerBase
{
    /// <summary>
    /// GET /api/organizer/events/{id}/tiers
    /// Trả về danh sách hạng vé của một event, sắp xếp theo giá tăng dần.
    /// </summary>
    [HttpGet("{id}/tiers")]
    public async Task<IActionResult> GetTiers([FromRoute] string id, CancellationToken ct)
    {
        var tiers = await db.TicketTiers
            .AsNoTracking()
            .Where(t => t.EventId == id)
            .OrderBy(t => t.Price)
            .ToListAsync(ct);

        if (tiers.Count == 0)
            return NotFound(new { message = $"No tiers found for event '{id}'." });

        return Ok(tiers);
    }
}
