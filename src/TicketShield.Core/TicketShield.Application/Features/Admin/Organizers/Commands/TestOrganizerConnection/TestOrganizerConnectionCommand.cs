using MediatR;
using TicketShield.Application.Common.Models;

namespace TicketShield.Application.Features.Admin.Organizers.Commands.TestOrganizerConnection;

public record TestOrganizerConnectionResult(
    Guid OrganizerId,
    string ConnectionStatus,
    long LatencyMs,
    string TargetEndpoint,
    string Message
)
{
    public string Status => ConnectionStatus;
    public string Endpoint => TargetEndpoint;
};

public record TestOrganizerConnectionCommand(Guid Id) : IRequest<ApiResponse<TestOrganizerConnectionResult>>;
