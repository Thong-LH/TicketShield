using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/v1/gate")]
public class GateAccessLogsController(OrganizerDbContext db) : ControllerBase
{
    [HttpGet("access-logs")]
    public async Task<IActionResult> GetAccessLogs([FromQuery] string? ticketCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketCode))
        {
            return BadRequest();
        }

        var code = ticketCode.Trim();
        var logs = await db.GateAccessLogs
            .AsNoTracking()
            .Where(row => row.TicketCode == code)
            .ToListAsync(cancellationToken);

        logs.Sort(static (left, right) =>
        {
            var byTime = left.ScannedAt.CompareTo(right.ScannedAt);
            return byTime != 0 ? byTime : left.Id.CompareTo(right.Id);
        });

        return Ok(logs);
    }
}
