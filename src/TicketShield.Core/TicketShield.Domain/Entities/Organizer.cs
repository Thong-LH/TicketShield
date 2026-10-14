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

    // Dynamic Gateway Connection Properties (US-7.5 / BE-CORE-7.5.1)
    public string? TaxCode { get; set; }
    public string? ContactPerson { get; set; }
    public string? ApiBaseUrl { get; set; }
    public string? GrpcEndpoint { get; set; }
    public string? WebhookSecretKey { get; set; }
    public string ConnectionStatus { get; set; } = "CONNECTED";
    public DateTimeOffset? LastPingAt { get; set; }
    public long? LastPingLatencyMs { get; set; }
    public DateTimeOffset? LastCatalogSyncedAt { get; set; }

    // Navigation
    public ICollection<Event> Events { get; set; } = new List<Event>();
}
