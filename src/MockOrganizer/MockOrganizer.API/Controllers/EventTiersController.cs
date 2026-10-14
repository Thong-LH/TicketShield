using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MockOrganizer.API.Data;

namespace MockOrganizer.API.Controllers;

[ApiController]
[Route("api/organizer/events")]
public class EventTiersController(OrganizerDbContext db) : ControllerBase
{
    /// <summary>
    /// GET /api/organizer/events
    /// Trả về toàn bộ danh mục sự kiện và hạng vé của BTC phục vụ đồng bộ dữ liệu (Catalog Ingestion)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetEventsCatalog([FromQuery] Guid? organizerId, CancellationToken ct)
    {
        var tiers = await db.TicketTiers
            .AsNoTracking()
            .ToListAsync(ct);

        var eventsMap = new Dictionary<string, (string Name, string Artist, string Category, string City, string Venue, string Desc, int DaysOffset)>
        {
            ["atsh-2026"] = (
                "Anh Trai Say Hi Concert 2026",
                "Anh Trai Say Hi All-Stars",
                "CONCERT",
                "Hà Nội",
                "Sân vận động Quốc gia Mỹ Đình",
                "Đêm nhạc quy tụ toàn bộ 30 Anh Trai với sân khấu hoành tráng hàng đầu Việt Nam.",
                30
            ),
            ["mixtape-hanoi-2026"] = (
                "SpaceSpeakers Mixtape Hanoi 2026",
                "SpaceSpeakers",
                "CONCERT",
                "Hà Nội",
                "Trung tâm Hội nghị Quốc gia",
                "Đêm hội Hip-hop đỉnh cao cùng Touliver, Binz, Soobin, Rhymastic.",
                45
            ),
            ["westlife-vn-2026"] = (
                "Westlife Vietnam Tour 2026",
                "Westlife",
                "CONCERT",
                "TP. Hồ Chí Minh",
                "Sân vận động Thống Nhất",
                "The Wild Dreams Tour - Đêm nhạc sống lại ký ức thanh xuân cùng Westlife.",
                60
            )
        };

        var now = DateTimeOffset.UtcNow;
        var catalog = eventsMap.Select(kv =>
        {
            var eventTiers = tiers
                .Where(t => t.EventId == kv.Key)
                .OrderBy(t => t.Price)
                .Select(t => new OrganizerTierCatalogDto(t.TierName, t.Price, $"Khu vực {t.TierName}"))
                .ToList();

            var startAt = now.AddDays(kv.Value.DaysOffset);
            return new OrganizerEventCatalogDto(
                kv.Key,
                kv.Value.Name,
                kv.Value.Artist,
                kv.Value.Category,
                kv.Value.City,
                kv.Value.Venue,
                kv.Value.Desc,
                startAt,
                startAt.AddHours(4),
                startAt.AddHours(-2),
                eventTiers
            );
        }).ToList();

        return Ok(catalog);
    }

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

public record OrganizerEventCatalogDto(
    string ExternalEventId,
    string Name,
    string Artist,
    string Category,
    string City,
    string Venue,
    string Description,
    DateTimeOffset EventStartAt,
    DateTimeOffset EventEndAt,
    DateTimeOffset ResaleDeadline,
    List<OrganizerTierCatalogDto> Tiers
);

public record OrganizerTierCatalogDto(
    string TierName,
    decimal Price,
    string? Description
);
