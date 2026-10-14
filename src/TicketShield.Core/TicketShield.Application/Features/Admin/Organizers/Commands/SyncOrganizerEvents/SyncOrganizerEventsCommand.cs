using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.SyncOrganizerEvents;

public record SyncOrganizerEventsResult(
    Guid OrganizerId,
    int EventsCount,
    int TiersCount,
    string Message,
    DateTimeOffset SyncedAt
);

public record SyncOrganizerEventsCommand(Guid Id) : IRequest<ApiResponse<SyncOrganizerEventsResult>>;
