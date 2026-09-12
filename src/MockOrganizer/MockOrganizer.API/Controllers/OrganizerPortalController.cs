using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;
using MockOrganizer.API.Entities;
using MockOrganizer.API.Resale;
using System.Security.Cryptography;
using System.Text;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer")]
public class OrganizerPortalController(
    OrganizerDbContext db,
    ResaleStore resaleStore,
    ResaleOptions options) : ControllerBase
{
    private string Hash(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.HmacKey), Encoding.UTF8.GetBytes(value)));

    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets([FromQuery] string? search, [FromQuery] string? status)
    {
        var query = db.MockTickets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.TicketCode.ToLower().Contains(s) || t.EventName.ToLower().Contains(s) || t.OwnerEmail.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(t => t.Status == status);
        }

        var list = await query.OrderByDescending(t => t.CreatedAt).Take(50).ToListAsync();
        return Ok(list);
    }

    [HttpPost("tickets/generate")]
    public async Task<IActionResult> GenerateTicket([FromBody] GenerateTicketRequest? request)
    {
        var randSuffix = RandomNumberGenerator.GetInt32(1000, 9999).ToString();
        var code = request?.TicketCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(code))
        {
            code = $"ATSH-VIP-{randSuffix}";
        }

        // Check if exists
        var existing = await db.MockTickets.FirstOrDefaultAsync(t => t.TicketCode == code);
        if (existing != null)
        {
            return Conflict(new { message = $"Ticket code {code} already exists." });
        }

        var zones = new[] { "VIP Zone A - Row 1 Seat 12", "VIP Zone B - Row 3 Seat 05", "GA Standing Zone 1", "Standard Zone C" };
        var randomZone = zones[RandomNumberGenerator.GetInt32(zones.Length)];

        var ticket = new MockTicket
        {
            Id = Guid.NewGuid(),
            TicketCode = code,
            EventName = string.IsNullOrWhiteSpace(request?.EventName) ? "Anh Trai Say Hi Concert 2026" : request.EventName,
            SeatZone = string.IsNullOrWhiteSpace(request?.SeatZone) ? randomZone : request.SeatZone,
            OriginalPrice = request?.OriginalPrice > 0 ? request.OriginalPrice : (code.Contains("VIP") ? 2500000 : 1200000),
            OwnerEmail = string.IsNullOrWhiteSpace(request?.OwnerEmail) ? "linhtranlatao2004@gmail.com" : request.OwnerEmail,
            OwnerPhone = "0901234567",
            OwnerName = "Nguyen Van Seller",
            Status = "VALID",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.MockTickets.Add(ticket);
        await db.SaveChangesAsync();

        // Ensure ticket mapping is available
        options.TicketMappings[ticket.TicketCode] = new TicketMapping
        {
            EventId = "concert-2026",
            TierId = code.Contains("VIP") ? "vip-zone-a" : "ga-zone"
        };

        return Ok(ticket);
    }

    [HttpPost("tickets/{code}/reset")]
    public async Task<IActionResult> ResetTicketStatus([FromRoute] string code)
    {
        var ticket = await db.MockTickets.FirstOrDefaultAsync(t => t.TicketCode == code);
        if (ticket == null) return NotFound(new { message = "Ticket not found." });

        ticket.Status = "VALID";
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        // Also release any lock in resale store
        var lockKey = "current:" + Hash(code);
        var currentLock = await resaleStore.Read<LockRecord>(lockKey, HttpContext.RequestAborted);
        if (currentLock != null)
        {
            currentLock.ReleasedAt = DateTimeOffset.UtcNow;
            await resaleStore.Put(lockKey, currentLock, HttpContext.RequestAborted);
            await resaleStore.SaveChangesAsync();
        }

        return Ok(ticket);
    }

    [HttpGet("otps")]
    public async Task<IActionResult> GetLatestOtps([FromQuery] int limit = 15)
    {
        var otps = await db.MockOtps
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync();
        return Ok(otps);
    }

    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs([FromQuery] int limit = 20)
    {
        var logs = await db.GateAccessLogs
            .AsNoTracking()
            .OrderByDescending(l => l.ScannedAt)
            .Take(limit)
            .ToListAsync();
        return Ok(logs);
    }

    [HttpPost("reset-all")]
    public async Task<IActionResult> ResetAllData()
    {
        await OrganizerDatabaseSeeder.SeedOrganizerAsync(db);
        return Ok(new { message = "Mock organizer database reset to seed data successfully." });
    }
}

public class GenerateTicketRequest
{
    public string? TicketCode { get; set; }
    public string? EventName { get; set; }
    public string? SeatZone { get; set; }
    public decimal OriginalPrice { get; set; }
    public string? OwnerEmail { get; set; }
}
