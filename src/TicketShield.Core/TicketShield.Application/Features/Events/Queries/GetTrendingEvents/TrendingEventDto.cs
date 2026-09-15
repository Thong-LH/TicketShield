namespace TicketShield.Application.Features.Events.Queries.GetTrendingEvents;

public class TrendingEventDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Artist { get; set; }
    public string? Category { get; set; }
    public string? City { get; set; }
    public string? BannerUrl { get; set; }
    public string Venue { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset EventStartAt { get; set; }
    public DateTimeOffset EventEndAt { get; set; }
    public decimal? MinResalePrice { get; set; }
    public decimal? OriginalPriceFrom { get; set; }
    public int TotalAvailableListings { get; set; }
    public string OrganizerName { get; set; } = string.Empty;
}
