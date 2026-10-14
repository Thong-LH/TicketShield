namespace TicketShield.Application.Features.Admin.Organizers.Models;

public record AdminOrganizerDto(
    Guid Id,
    string Name,
    string OfficialEmail,
    string? ContactPhone,
    string? TaxCode,
    string? ContactPerson,
    string Status,
    string? ApiBaseUrl,
    string? GrpcEndpoint,
    string? WebhookUrl,
    string? WebhookSecretKey,
    string ConnectionStatus,
    int EventsCount,
    DateTimeOffset? LastPingAt,
    long? LastPingLatencyMs,
    DateTimeOffset? LastCatalogSyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    List<AdminOrganizerEventItemDto>? Events = null
);

public record AdminOrganizerEventItemDto(
    Guid Id,
    string Name,
    string? Artist,
    string? Venue,
    string? City,
    DateTimeOffset EventStartAt,
    string Status,
    int TiersCount
);
