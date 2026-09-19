using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/v1/mock-tickets")]
public class MockTicketsController(OrganizerDbContext db) : ControllerBase
{
    /// <summary>
    /// Mock Organizer API for Buyer to query their newly issued/transferred tickets.
    /// GET /api/v1/mock-tickets/my-tickets?email=buyer@example.com&status=VALID
    /// </summary>
    [HttpGet("my-tickets")]
    public async Task<IActionResult> GetMyTickets(
        [FromQuery] string? email,
        [FromQuery] string? ownerEmail,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        var targetEmail = email ?? ownerEmail;
        if (string.IsNullOrWhiteSpace(targetEmail))
        {
            if (Request.Headers.TryGetValue("X-User-Email", out var headerEmail) && !string.IsNullOrWhiteSpace(headerEmail))
            {
                targetEmail = headerEmail.ToString();
            }
            else if (Request.Headers.TryGetValue("X-Buyer-Email", out var headerBuyerEmail) && !string.IsNullOrWhiteSpace(headerBuyerEmail))
            {
                targetEmail = headerBuyerEmail.ToString();
            }
        }

        var query = db.MockTickets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(targetEmail))
        {
            var e = targetEmail.Trim().ToLowerInvariant();
            query = query.Where(t => t.OwnerEmail.ToLower() == e);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            query = query.Where(t => t.Status.ToUpper() == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.TicketCode.ToLower().Contains(q)
                                  || t.EventName.ToLower().Contains(q)
                                  || t.SeatZone.ToLower().Contains(q));
        }

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        return Ok(new
        {
            success = true,
            count = tickets.Count,
            data = tickets
        });
    }

    /// <summary>
    /// GET /api/v1/mock-tickets/{ticketCode}
    /// Lookup specific ticket details by ticket code.
    /// </summary>
    [HttpGet("{ticketCode}")]
    public async Task<IActionResult> GetTicketByCode(string ticketCode)
    {
        if (string.IsNullOrWhiteSpace(ticketCode))
        {
            return BadRequest(new { success = false, message = "Ticket code is required." });
        }

        var code = ticketCode.Trim();
        var ticket = await db.MockTickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TicketCode == code);

        if (ticket == null)
        {
            return NotFound(new { success = false, message = $"Ticket with code '{ticketCode}' not found." });
        }

        return Ok(new
        {
            success = true,
            data = ticket
        });
    }
}
