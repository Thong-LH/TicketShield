using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class Organizer : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string OfficialEmail { get; set; } = string.Empty;
    public string? ContactPhone { get; set; }
    public string ApiKeyHash { get; set; } = string.Empty;
    public string? WebhookUrl { get; set; }
    public string Status { get; set; } = "ACTIVE";

    // Navigation
    public ICollection<Event> Events { get; set; } = new List<Event>();
}
